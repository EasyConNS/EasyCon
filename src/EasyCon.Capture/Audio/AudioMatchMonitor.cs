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
    public AudioMatchSettings(int threshold = 85, double cooldownSeconds = 1)
    {
        if (threshold is < 10 or > 100)
            throw new ArgumentOutOfRangeException(nameof(threshold));
        if (!double.IsFinite(cooldownSeconds) || cooldownSeconds is < 0 or > 30)
            throw new ArgumentOutOfRangeException(nameof(cooldownSeconds));
        Threshold = threshold;
        CooldownSeconds = cooldownSeconds;
    }

    public int Threshold { get; }
    public double CooldownSeconds { get; }
}

public sealed record AudioMatchHit(DateTimeOffset DetectedAt, int Score, int Threshold);
public sealed record AudioMatchUpdate(AudioMatchStatus Status, int? Score, IReadOnlyList<AudioMatchHit> Hits)
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
    private readonly bool _requireContinuousSession;
    private readonly long _start;
    private readonly AudioCaptureState? _loadedSession;
    private string? _failure;
    private long _lastTimestampNs;
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
    private int? _lastScore;
    private readonly AudioMatchTrigger _trigger = new();

    public AudioMatchMonitor(IAudioCaptureSource source, AudioMatchTemplate template)
        : this(source, template, Stopwatch.GetTimestamp)
    {
    }

    internal AudioMatchMonitor(IAudioCaptureSource source, AudioMatchTemplate template, Func<long> clock,
        bool requireContinuousSession = false)
    {
        _source = source;
        _template = template;
        _clock = clock;
        _requireContinuousSession = requireContinuousSession;
        _start = clock();
        if (requireContinuousSession)
        {
            _loadedSession = source.State;
            if (!source.IsSupported || _loadedSession.Status != AudioCaptureStatus.Connected || _loadedSession.Format == null)
                throw new InvalidOperationException("请先连接游戏音频，再使用 AUDIO_LOAD。");
            _session = _loadedSession;
        }
        _source.DataAvailable += OnDataAvailable;
        _source.StateChanged += OnStateChanged;
        try { CheckHealth(); }
        catch
        {
            Dispose();
            throw;
        }
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
            if (_disposed || _failure != null)
                return;
            AudioCaptureState state = _source.State;
            if (_requireContinuousSession && !ReferenceEquals(state, _loadedSession))
            {
                ResetLocked(AudioMatchStatus.Disconnected, state);
                return;
            }
            // Check identity before validation: even a malformed old packet must leave new audio intact.
            if (chunk.Session != null && !ReferenceEquals(chunk.Session, state))
                return;
            long now = _clock();
            if (_requireContinuousSession)
            {
                if (chunk.TimestampNs <= AudioClock.ToNanoseconds(_start))
                    return;
                if (chunk.Session == null || chunk.TimestampNs <= _lastTimestampNs
                    || chunk.TimestampNs > AudioClock.ToNanoseconds(now)
                    || AudioClock.ToNanoseconds(now) - chunk.TimestampNs > 1_000_000_000L)
                {
                    ResetLocked(AudioMatchStatus.InvalidAudio, state);
                    return;
                }
            }
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
            if (!ReferenceEquals(_session, state))
                ResetLocked(AudioMatchStatus.WarmingUp, state);
            else if ((_lastPacket != 0 && now - _lastPacket > Stopwatch.Frequency)
                || _pending.Count >= 256 || _queuedBytes + chunk.BytesRecorded > MaximumQueuedBytes
                || _queuedSeconds + seconds > MaximumQueuedSeconds)
                ResetLocked(AudioMatchStatus.Interrupted, state);
            if (_failure != null)
                return;
            // A callback can straddle LOAD. Keep only complete sample frames after its boundary.
            int skip = _requireContinuousSession
                ? (int)Math.Clamp(Math.Ceiling((AudioClock.ToNanoseconds(_start) - chunk.TimestampNs)
                    * (chunk.Format.SampleRate / 1_000_000_000d) + chunk.BytesRecorded / alignment), 0, chunk.BytesRecorded / alignment)
                : 0;
            int bytes = chunk.BytesRecorded - skip * alignment;
            if (bytes > 0)
            {
                _pending.Enqueue(chunk.Pcm.Span.Slice(skip * alignment, bytes).ToArray());
                _queuedBytes += bytes;
                _queuedSeconds += bytes / (double)alignment / chunk.Format.SampleRate;
            }
            _lastPacket = now;
            _lastTimestampNs = chunk.TimestampNs;
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
        if (_requireContinuousSession && !_disposed)
        {
            if (!ReferenceEquals(state, _loadedSession))
                _failure ??= "音频源已断开或重新连接，请重新运行脚本。";
            else if (status == AudioMatchStatus.InvalidAudio)
                _failure ??= "识别收到无效音频、格式变化或不连续的时间戳。";
            else if (status == AudioMatchStatus.Interrupted)
                _failure ??= "音频输入中断或后台分析积压，识别已停止，请检查输入和处理负载。";
        }
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
        CheckHealth();
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
        int? score = null;
        AudioMatchSettings settings = Settings;
        try
        {
            foreach (byte[] packet in packets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _matcher ??= new AudioTemplateMatcher(_template, state.Format!);
                _matcher.Append(packet, match =>
                {
                    int similarity = ToPercentage(match.Score);
                    score = Math.Max(score ?? 0, similarity);
                    if (_trigger.Observe(similarity, match.EndSeconds, settings))
                        hits.Add(new AudioMatchHit(DateTimeOffset.Now, similarity, settings.Threshold));
                }, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            lock (_gate)
            {
                if (_epoch == epoch)
                    ResetLocked(AudioMatchStatus.InvalidAudio, state);
                CheckHealth();
                return new AudioMatchUpdate(AudioMatchStatus.InvalidAudio, null, []) { Generation = _epoch };
            }
        }

        // A disconnect/overflow during analysis invalidates the whole in-flight batch, including hits.
        lock (_gate)
        {
            CheckHealth();
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

    /// <summary>Quantize normalized matcher output once for scores, peaks and hit decisions.</summary>
    internal static int ToPercentage(double score) => (int)Math.Floor(score * 100);

    /// <summary>Reject a result if its source changed while the consumer was dispatching it.</summary>
    public bool IsCurrent(AudioMatchUpdate update)
    {
        lock (_gate)
            return !_disposed && _epoch == update.Generation && ReferenceEquals(_session, _source.State)
                && (_lastPacket == 0 || _clock() - _lastPacket <= Stopwatch.Frequency);
    }

    /// <summary>Script sessions keep a terminal failure; the diagnostic UI may instead recover.</summary>
    public void CheckHealth()
    {
        if (!_requireContinuousSession)
            return;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!ReferenceEquals(_source.State, _loadedSession))
                _failure ??= "音频源已断开或重新连接，请重新运行脚本。";
            if (_clock() - (_lastPacket == 0 ? _start : _lastPacket) > Stopwatch.Frequency)
                _failure ??= "音频输入超过 1 秒没有新数据，请检查输入并重新运行脚本。";
            if (_failure != null)
                throw new InvalidOperationException(_failure);
        }
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

    public bool Observe(int score, double endSeconds, AudioMatchSettings settings)
    {
        // Rearm only after the signal falls. Cooldown alone would repeatedly report one long sound.
        if (score < Math.Max(0, settings.Threshold - 5))
            _armed = true;
        if (!_armed || score < settings.Threshold || endSeconds - _lastHit < settings.CooldownSeconds)
            return false;
        _armed = false;
        _lastHit = endSeconds;
        return true;
    }

    public void Reset()
    {
        _armed = true;
        _lastHit = double.NegativeInfinity;
    }
}