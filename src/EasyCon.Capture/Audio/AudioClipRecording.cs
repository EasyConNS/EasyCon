namespace EasyCon.Capture.Audio;

/// <summary>A finite recording of new game audio. Never owns or disconnects the capture device.</summary>
public sealed class AudioClipRecording : IDisposable
{
    private readonly IAudioCaptureSource _source;
    private readonly AudioCaptureState _session;
    private readonly AudioFormat _format;
    private readonly Func<long> _clock;
    private readonly long _start;
    private readonly int _alignment;
    private readonly int _maximumBytes;
    private readonly object _gate = new();
    private readonly MemoryStream _pcm = new();
    private readonly TaskCompletionSource<AudioClip> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Timer? _watchdog;
    private long _lastData;
    private bool _disposed;

    public AudioClipRecording(IAudioCaptureSource source)
        : this(source, AudioDetectionSession.Now, true)
    {
    }

    internal AudioClipRecording(IAudioCaptureSource source, Func<long> clock, bool watchClock)
    {
        _source = source;
        _clock = clock;
        _start = _lastData = clock();
        _session = source.State;
        if (!source.IsSupported || _session.Status != AudioCaptureStatus.Connected || _session.Format == null)
            throw new InvalidOperationException("请先连接游戏音频，再开始录制。");
        _format = _session.Format;
        _alignment = AudioClip.GetBlockAlign(_format);
        _maximumBytes = (int)Math.Min((long)_format.SampleRate * 15, AudioClip.MaximumBytes / _alignment) * _alignment;
        _source.DataAvailable += OnData;
        _source.StateChanged += OnState;
        if (!ReferenceEquals(_source.State, _session))
            Fail("录制期间音频源已断开或重新连接，请重新录制。");
        if (watchClock)
            _watchdog = new Timer(_ => CheckInput(), null, 100, 100);
    }

    public Task<AudioClip> Completion => _completion.Task;
    public double MaximumSeconds => _maximumBytes / (double)_alignment / _format.SampleRate;
    public double Duration { get { lock (_gate) return _pcm.Length / (double)_alignment / _format.SampleRate; } }
    public bool ReachedLimit { get; private set; }

    public void Stop()
    {
        lock (_gate)
        {
            CheckInput();
            Finish();
        }
    }

    internal void CheckInput()
    {
        lock (_gate)
        {
            if (_disposed || Completion.IsCompleted)
                return;
            long now = _clock();
            if (!ReferenceEquals(_source.State, _session))
                Fail("录制期间音频源已断开或重新连接，请重新录制。");
            else if (now - _lastData > 1_000_000_000L)
                Fail("录制期间超过 1 秒没有音频数据，请检查输入并重新录制。");
            else if (now - _start >= MaximumSeconds * 1_000_000_000L)
            {
                ReachedLimit = true;
                Finish();
            }
        }
    }

    private void OnData(AudioChunk chunk)
    {
        lock (_gate)
        {
            if (_disposed || Completion.IsCompleted)
                return;
            if (!ReferenceEquals(_source.State, _session))
            {
                Fail("录制期间音频源已断开或重新连接，请重新录制。");
                return;
            }
            // Ignore callbacks from older sessions or audio already captured before the click.
            if ((chunk.Session != null && !ReferenceEquals(chunk.Session, _session))
                || (chunk.TimestampNs > 0 && chunk.TimestampNs <= _start))
                return;
            if (chunk.Format != _format || chunk.BytesRecorded <= 0 || chunk.BytesRecorded > chunk.Pcm.Length
                || chunk.BytesRecorded % _alignment != 0)
            {
                Fail("录制收到无效音频或格式变化，请重新录制。");
                return;
            }
            long now = _clock();
            if (now - _lastData > 1_000_000_000L)
            {
                Fail("录制期间音频输入中断，请重新录制。");
                return;
            }
            _lastData = now;
            int frames = chunk.BytesRecorded / _alignment;
            long skip = chunk.TimestampNs > 0
                ? Math.Clamp((long)Math.Ceiling((_start - chunk.TimestampNs) * (_format.SampleRate / 1_000_000_000d) + frames), 0, frames)
                : 0;
            int offset = (int)skip * _alignment;
            int count = (int)Math.Min(chunk.BytesRecorded - offset, _maximumBytes - _pcm.Length);
            _pcm.Write(chunk.Pcm.Span.Slice(offset, count));
            if (_pcm.Length == _maximumBytes)
            {
                ReachedLimit = true;
                Finish();
            }
        }
    }

    private void OnState(AudioCaptureState state)
    {
        lock (_gate)
        {
            if (!_disposed && !ReferenceEquals(_source.State, _session))
                Fail("录制期间音频源已断开或重新连接，请重新录制。");
        }
    }

    private void Fail(string message) => _completion.TrySetException(new InvalidOperationException(message));

    private void Finish()
    {
        if (_disposed || Completion.IsCompleted)
            return;
        if (_pcm.Length == 0)
            Fail("尚未录到声音，请确认输入有数据后再录制。");
        else
            _completion.TrySetResult(new AudioClip(_format, _pcm.GetBuffer().AsSpan(0, (int)_pcm.Length)));
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _completion.TrySetCanceled();
            _pcm.Dispose();
        }
        _watchdog?.Dispose();
        _source.DataAvailable -= OnData;
        _source.StateChanged -= OnState;
    }
}