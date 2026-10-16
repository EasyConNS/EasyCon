using System.Diagnostics;

namespace EasyCon.Capture.Audio;

public enum AudioMatchStatus
{
    WaitingForAudio,
    WarmingUp,
    Listening,
    Interrupted,
    Disconnected,
    InvalidAudio,
}

public sealed record AudioMatchSettings
{
    public AudioMatchSettings(double threshold = 0.85, double cooldownSeconds = 1)
    {
        if (!double.IsFinite(threshold) || threshold is < 0.1 or > 1)
            throw new ArgumentOutOfRangeException(nameof(threshold));
        if (!double.IsFinite(cooldownSeconds) || cooldownSeconds is < 0 or > 30)
            throw new ArgumentOutOfRangeException(nameof(cooldownSeconds));
        Threshold = threshold;
        CooldownSeconds = cooldownSeconds;
    }

    public double Threshold { get; }
    public double CooldownSeconds { get; }
}

public sealed record AudioMatchHit(DateTimeOffset DetectedAt, double Score, double Threshold);
public sealed record AudioMatchUpdate(AudioMatchStatus Status, double? Score, IReadOnlyList<AudioMatchHit> Hits)
{
    internal long Generation { get; init; } = -1;
}

/// <summary>
/// Borrows an existing capture source. Call Poll on a single background consumer.
/// The callback only copies into a bounded queue; FFT and comparison never run on capture/UI threads.
/// Callback arrival times detect gross stalls, not hardware discontinuities or script timing boundaries.
/// </summary>
public sealed class AudioMatchMonitor : IDisposable
{
    private const int MaximumQueuedBytes = 4 * 1024 * 1024;
    private const double MaximumQueuedSeconds = 2;
    private readonly IAudioCaptureSource _source;
    private readonly AudioMatchTemplate _template;
    private readonly Func<long> _clock;
    private readonly object _gate = new();
    private readonly Queue<byte[]> _pending = new();
    private AudioCaptureState? _session;
    private AudioMatchStatus _resetStatus = AudioMatchStatus.WaitingForAudio;
    private int _queuedBytes;
    private double _queuedSeconds;
    private long _lastPacket;
    private long _epoch;
    private long _processedEpoch = -1;
    private bool _disposed;
    private AudioMatchSettings _settings = new();
    private AudioTemplateMatcher? _matcher;
    private double? _lastScore;
    private readonly AudioMatchTrigger _trigger = new();

    public AudioMatchMonitor(IAudioCaptureSource source, AudioMatchTemplate template)
        : this(source, template, Stopwatch.GetTimestamp)
    {
    }

    internal AudioMatchMonitor(IAudioCaptureSource source, AudioMatchTemplate template, Func<long> clock)
    {
        _source = source;
        _template = template;
        _clock = clock;
        _source.DataAvailable += OnDataAvailable;
        _source.StateChanged += OnStateChanged;
    }

    public AudioMatchSettings Settings
    {
        get => Volatile.Read(ref _settings);
        set => Volatile.Write(ref _settings, value ?? throw new ArgumentNullException(nameof(value)));
    }

    private void OnDataAvailable(AudioChunk chunk)
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            AudioCaptureState state = _source.State;
            // Check identity before validation: even a malformed old packet must leave new audio intact.
            if (chunk.Session != null && !ReferenceEquals(chunk.Session, state))
                return;
            if (state.Status != AudioCaptureStatus.Connected || state.Format != chunk.Format)
            {
                ResetLocked(AudioMatchStatus.Interrupted, state);
                return;
            }
            int alignment;
            try { alignment = AudioClip.GetBlockAlign(chunk.Format); }
            catch (ArgumentException)
            {
                ResetLocked(AudioMatchStatus.InvalidAudio, state);
                return;
            }
            double seconds = chunk.BytesRecorded / (double)alignment / chunk.Format.SampleRate;
            if (chunk.BytesRecorded <= 0 || chunk.BytesRecorded > chunk.Pcm.Length || chunk.BytesRecorded % alignment != 0
                || chunk.BytesRecorded > MaximumQueuedBytes || seconds > MaximumQueuedSeconds)
            {
                ResetLocked(AudioMatchStatus.InvalidAudio, state);
                return;
            }
            long now = _clock();
            if (!ReferenceEquals(_session, state))
                ResetLocked(AudioMatchStatus.WarmingUp, state);
            else if ((_lastPacket != 0 && now - _lastPacket > Stopwatch.Frequency)
                || _pending.Count >= 256 || _queuedBytes + chunk.BytesRecorded > MaximumQueuedBytes
                || _queuedSeconds + seconds > MaximumQueuedSeconds)
                ResetLocked(AudioMatchStatus.Interrupted, state);
            _pending.Enqueue(chunk.Pcm.Span[..chunk.BytesRecorded].ToArray());
            _queuedBytes += chunk.BytesRecorded;
            _queuedSeconds += seconds;
            _lastPacket = now;
        }
    }

    private void OnStateChanged(AudioCaptureState state)
    {
        lock (_gate)
        {
            if (!_disposed && ReferenceEquals(state, _source.State) && !ReferenceEquals(state, _session))
                ResetLocked(state.Status == AudioCaptureStatus.Connected ? AudioMatchStatus.WarmingUp : AudioMatchStatus.Disconnected, state);
        }
    }

    private void ResetLocked(AudioMatchStatus status, AudioCaptureState? state)
    {
        _epoch++;
        _pending.Clear();
        _queuedBytes = 0;
        _queuedSeconds = 0;
        _lastPacket = 0;
        _session = state;
        _resetStatus = status;
    }

    public AudioMatchUpdate Poll(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AudioCaptureState state;
        byte[][] packets;
        long epoch;
        AudioMatchStatus resetStatus;
        lock (_gate)
        {
            if (_disposed)
                return new AudioMatchUpdate(AudioMatchStatus.Disconnected, null, []) { Generation = _epoch };
            state = _source.State;
            if (!ReferenceEquals(state, _session))
                ResetLocked(state.Status == AudioCaptureStatus.Connected ? AudioMatchStatus.WaitingForAudio : AudioMatchStatus.Disconnected, state);
            if (_lastPacket != 0 && _clock() - _lastPacket > Stopwatch.Frequency)
                ResetLocked(AudioMatchStatus.Interrupted, state);
            packets = _pending.ToArray();
            _pending.Clear();
            _queuedBytes = 0;
            _queuedSeconds = 0;
            epoch = _epoch;
            resetStatus = _resetStatus;
        }

        bool reset = _processedEpoch != epoch;
        if (reset)
        {
            _processedEpoch = epoch;
            _matcher = null;
            _lastScore = null;
            _trigger.Reset();
        }
        List<AudioMatchHit> hits = [];
        double? score = null;
        AudioMatchSettings settings = Settings;
        try
        {
            foreach (byte[] packet in packets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _matcher ??= new AudioTemplateMatcher(_template, state.Format!);
                _matcher.Append(packet, match =>
                {
                    score = Math.Max(score ?? 0, match.Score);
                    if (_trigger.Observe(match, settings))
                        hits.Add(new AudioMatchHit(DateTimeOffset.Now, match.Score, settings.Threshold));
                }, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            lock (_gate)
            {
                if (_epoch == epoch)
                    ResetLocked(AudioMatchStatus.InvalidAudio, state);
                return new AudioMatchUpdate(AudioMatchStatus.InvalidAudio, null, []) { Generation = _epoch };
            }
        }

        // A disconnect/overflow during analysis invalidates the whole in-flight batch, including hits.
        lock (_gate)
        {
            if (_disposed || _epoch != epoch || !ReferenceEquals(state, _source.State))
                return new AudioMatchUpdate(AudioMatchStatus.Interrupted, null, []) { Generation = _epoch };
        }
        if (score.HasValue)
            _lastScore = score;
        AudioMatchStatus status = _lastScore.HasValue ? AudioMatchStatus.Listening
            : _matcher != null ? AudioMatchStatus.WarmingUp : resetStatus;
        if (reset && resetStatus is AudioMatchStatus.Interrupted or AudioMatchStatus.InvalidAudio)
            status = resetStatus;
        return new AudioMatchUpdate(status, _lastScore, hits) { Generation = epoch };
    }

    /// <summary>Reject a result if its source changed while the consumer was dispatching it.</summary>
    public bool IsCurrent(AudioMatchUpdate update)
    {
        lock (_gate)
            return !_disposed && _epoch == update.Generation && ReferenceEquals(_session, _source.State)
                && (_lastPacket == 0 || _clock() - _lastPacket <= Stopwatch.Frequency);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            ResetLocked(AudioMatchStatus.Disconnected, null);
        }
        _source.DataAvailable -= OnDataAvailable;
        _source.StateChanged -= OnStateChanged;
    }
}

internal sealed class AudioMatchTrigger
{
    private bool _armed = true;
    private double _lastHit = double.NegativeInfinity;

    public bool Observe(AudioMatchScore match, AudioMatchSettings settings)
    {
        // Rearm only after the signal falls. Cooldown alone would repeatedly report one long sound.
        if (match.Score < Math.Max(0, settings.Threshold - 0.05))
            _armed = true;
        if (!_armed || match.Score < settings.Threshold || match.EndSeconds - _lastHit < settings.CooldownSeconds)
            return false;
        _armed = false;
        _lastHit = match.EndSeconds;
        return true;
    }

    public void Reset()
    {
        _armed = true;
        _lastHit = double.NegativeInfinity;
    }
}