using System.Diagnostics;

namespace EasyCon.Capture.Audio;

/// <summary>
/// Bounded, in-memory history for clip editing. Borrows the existing source;
/// never opens a device or owns its connection. Timestamps are callback times,
/// not hardware sample timestamps, and must not be used for script detection.
/// </summary>
public sealed class AudioHistoryBuffer : IDisposable
{
    private readonly IAudioCaptureSource _source;
    private readonly object _gate = new();
    private readonly double _seconds;
    private readonly Func<long> _clock;
    private byte[] _buffer = [];
    private AudioCaptureState? _session;
    private AudioFormat? _format;
    private int _write;
    private int _length;
    private long _lastPacket;
    private bool _disposed;

    public bool IsSupported => _source.IsSupported;

    public AudioClipRecording BeginRecording()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return new AudioClipRecording(_source);
        }
    }

    public AudioHistoryBuffer(IAudioCaptureSource source, double seconds = 15)
        : this(source, seconds, Stopwatch.GetTimestamp)
    {
    }

    internal AudioHistoryBuffer(IAudioCaptureSource source, double seconds, Func<long> clock)
    {
        if (!double.IsFinite(seconds) || seconds < 0.001 || seconds > 15)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        _source = source;
        _seconds = seconds;
        _clock = clock;
        _source.DataAvailable += OnDataAvailable;
        _source.StateChanged += OnStateChanged;
    }

    public AudioClip? Snapshot(double seconds = 10)
    {
        if (!double.IsFinite(seconds) || seconds <= 0 || seconds > _seconds)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        lock (_gate)
        {
            if (_disposed || _format == null || _length == 0 || !ReferenceEquals(_session, _source.State)
                || _source.State.Status != AudioCaptureStatus.Connected
                || _clock() - _lastPacket > Stopwatch.Frequency)
                return null;
            int alignment = AudioClip.GetBlockAlign(_format);
            int frames = (int)Math.Min(_length / alignment, Math.Floor(seconds * _format.SampleRate));
            if (frames == 0)
                return null;
            byte[] pcm = new byte[frames * alignment];
            int start = (_write - pcm.Length + _buffer.Length) % _buffer.Length;
            int first = Math.Min(pcm.Length, _buffer.Length - start);
            _buffer.AsSpan(start, first).CopyTo(pcm);
            _buffer.AsSpan(0, pcm.Length - first).CopyTo(pcm.AsSpan(first));
            return new AudioClip(_format, pcm);
        }
    }

    private void OnDataAvailable(AudioChunk chunk)
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            AudioCaptureState state = _source.State;
            if (state.Status != AudioCaptureStatus.Connected || state.Format != chunk.Format)
            {
                Clear();
                return;
            }
            int alignment;
            try
            {
                alignment = AudioClip.GetBlockAlign(chunk.Format);
            }
            catch (ArgumentException)
            {
                Clear();
                return;
            }
            if (chunk.BytesRecorded <= 0 || chunk.BytesRecorded > chunk.Pcm.Length || chunk.BytesRecorded % alignment != 0)
            {
                Clear();
                return;
            }
            long now = _clock();
            if (!ReferenceEquals(_session, state) || _format != chunk.Format || now - _lastPacket > Stopwatch.Frequency)
            {
                Clear();
                _session = state;
                _format = chunk.Format;
                int frames = (int)Math.Min(Math.Floor(_seconds * chunk.Format.SampleRate), AudioClip.MaximumBytes / alignment);
                _buffer = new byte[frames * alignment];
            }
            ReadOnlySpan<byte> data = chunk.Pcm.Span[..chunk.BytesRecorded];
            if (data.Length > _buffer.Length)
                data = data[^_buffer.Length..];
            int first = Math.Min(data.Length, _buffer.Length - _write);
            data[..first].CopyTo(_buffer.AsSpan(_write));
            data[first..].CopyTo(_buffer);
            _write = (_write + data.Length) % _buffer.Length;
            _length = Math.Min(_length + data.Length, _buffer.Length);
            _lastPacket = now;
        }
    }

    private void OnStateChanged(AudioCaptureState state)
    {
        lock (_gate)
        {
            // An old queued disconnect must not erase a newer connection's history.
            if (ReferenceEquals(state, _source.State) && state.Status != AudioCaptureStatus.Connected)
                Clear();
        }
    }

    private void Clear()
    {
        _buffer = [];
        _format = null;
        _session = null;
        _write = 0;
        _length = 0;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            Clear();
        }
        _source.DataAvailable -= OnDataAvailable;
        _source.StateChanged -= OnStateChanged;
    }
}