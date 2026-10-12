using System.Diagnostics;

namespace EasyCon.Capture.Audio;

/// <summary>
/// Per-run audio detection. Borrows an already connected source; never opens or stops a device.
/// Sample positions use a callback-clock anchor, NOT hardware capture timestamps.
/// All DSP uses the same AudioTemplateMatcher as the diagnostic window.
/// </summary>
public sealed class AudioDetectionSession : IDisposable
{
    public const int MaximumLookbackMs = 15_000;
    public const int MaximumTimeoutMs = 300_000;
    private readonly IAudioCaptureSource _source;
    private readonly Func<string, CancellationToken, AudioMatchTemplate> _load;
    private readonly Func<long> _clock;
    private readonly Action<int, CancellationToken> _delay;
    private readonly Timeline _history;

    public AudioDetectionSession(IAudioCaptureSource source, Func<string, CancellationToken, AudioMatchTemplate> load)
        : this(source, load, Now, (ms, token) =>
        {
            if (token.WaitHandle.WaitOne(ms))
                token.ThrowIfCancellationRequested();
        })
    {
    }

    internal AudioDetectionSession(IAudioCaptureSource source, Func<string, CancellationToken, AudioMatchTemplate> load,
        Func<long> clock, Action<int, CancellationToken> delay)
    {
        _source = source;
        _load = load;
        _clock = clock;
        _delay = delay;
        _history = new Timeline(source, clock, clock());
    }

    public double Match(string labelName, int lookbackMs, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (lookbackMs is < 1 or > MaximumLookbackMs)
            throw new ArgumentOutOfRangeException(nameof(lookbackMs), "AUDIO 回看范围必须为 1～15000 毫秒。");
        long end = _clock();
        // Freeze the window before file loading/DSP: neither can move a query into future audio.
        Segment[] segments = _history.Read(Math.Max(_history.Start, end - lookbackMs * 1_000_000L), end);
        AudioMatchTemplate template = _load(labelName, token);
        AudioTemplateMatcher matcher = new(template, _history.Format);
        double? peak = null;
        foreach (Segment segment in segments)
            matcher.Append(segment.Pcm.Span, score => peak = Math.Max(peak ?? 0, score.Score), token);
        _history.Check();
        token.ThrowIfCancellationRequested();
        return peak ?? throw new InvalidOperationException("AUDIO 有效历史不足一个完整模板；请先等待音频积累，或增大回看范围。");
    }

    public bool Wait(string labelName, int timeoutMs, double threshold = 0.85, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (timeoutMs is < 1 or > MaximumTimeoutMs)
            throw new ArgumentOutOfRangeException(nameof(timeoutMs), "AUDIO_WAIT 超时必须为 1～300000 毫秒。");
        // Shared validation with the UI; no percent conversion, clamping or rounding.
        _ = new AudioMatchSettings(threshold);
        long start = _clock();
        long deadline = start + timeoutMs * 1_000_000L;
        _history.Check();
        using Timeline pending = new(_source, _clock, start);
        AudioMatchTemplate template = _load(labelName, token);
        AudioTemplateMatcher matcher = new(template, pending.Format);
        long cursor = start;
        bool scored = false;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            _history.Check();
            long now = _clock();
            Segment[] segments = pending.Read(cursor, Math.Min(now, deadline), deadline);
            if (now >= deadline)
            {
                // Do not let queued work extend the deadline, or turn unprocessed audio into a negative result.
                if (segments.Length != 0)
                    throw new InvalidOperationException("AUDIO_WAIT 音频分析积压，未能在截止时间前完成。");
                pending.RequireCoverage(deadline);
                if (!scored)
                    throw new InvalidOperationException("AUDIO_WAIT 有效音频不足一个完整模板，不能判定为未命中。");
                return false;
            }
            foreach (Segment segment in segments)
            {
                bool hit = false;
                matcher.Append(segment.Pcm.Span, score =>
                {
                    scored = true;
                    hit |= score.Score >= threshold;
                }, token);
                token.ThrowIfCancellationRequested();
                pending.Check();
                _history.Check();
                if (_clock() >= deadline)
                    throw new InvalidOperationException("AUDIO_WAIT 音频分析超过截止时间，结果已作废。");
                if (hit)
                    return true;
                cursor = segment.End;
            }
            int remainingMs = (int)Math.Max(1, (deadline - _clock()) / 1_000_000);
            _delay(Math.Min(10, remainingMs), token);
        }
    }

    public void Dispose() => _history.Dispose();

    internal static long Now()
    {
        long ticks = Stopwatch.GetTimestamp();
        return (ticks / Stopwatch.Frequency * 1_000_000_000L)
            + (ticks % Stopwatch.Frequency * 1_000_000_000L / Stopwatch.Frequency);
    }

    private sealed record Segment(ReadOnlyMemory<byte> Pcm, long End);

    /// <summary>
    /// Bounded owned PCM with a monotonic sample timeline and immutable device-session identity.
    /// A fault is terminal for this script run: reconnecting must not silently heal missing evidence.
    /// </summary>
    private sealed class Timeline : IDisposable
    {
        private const long ToleranceNs = 250_000_000;
        private const int MaximumBytes = 16 * 1024 * 1024;
        private readonly IAudioCaptureSource _source;
        private readonly AudioCaptureState _session;
        private readonly Func<long> _clock;
        private readonly object _gate = new();
        private readonly Queue<Packet> _packets = new();
        private readonly int _alignment;
        private long? _origin;
        private long _frames;
        private long _lastArrival;
        private long _firstArrival;
        private long _evictedEnd;
        private int _bytes;
        private string? _error;
        private bool _disposed;

        private sealed record Packet(long Frame, byte[] Pcm, long Arrival);

        public Timeline(IAudioCaptureSource source, Func<long> clock, long start)
        {
            _source = source;
            _clock = clock;
            Start = start;
            _session = source.State;
            if (!source.IsSupported || _session.Status != AudioCaptureStatus.Connected || _session.Format == null)
                throw new InvalidOperationException("脚本音频需要先连接主窗口的游戏音频输入。");
            Format = _session.Format;
            _alignment = AudioClip.GetBlockAlign(Format);
            source.DataAvailable += OnData;
            source.StateChanged += OnState;
            // State may change between the snapshot and subscribing. Do not leak a failed construction.
            try { Check(); }
            catch
            {
                Dispose();
                throw;
            }
        }

        public long Start { get; }
        public AudioFormat Format { get; }

        private long TimeAt(long frame) => _origin!.Value + (frame / Format.SampleRate * 1_000_000_000L)
            + (frame % Format.SampleRate * 1_000_000_000L / Format.SampleRate);

        private long FrameAt(long time, bool ceiling)
        {
            long seconds = Math.DivRem(time - _origin!.Value, 1_000_000_000L, out long nanos);
            long frames = seconds * Format.SampleRate + Math.DivRem(nanos * Format.SampleRate, 1_000_000_000L, out long remainder);
            return frames + (ceiling && remainder > 0 ? 1 : !ceiling && remainder < 0 ? -1 : 0);
        }

        private void OnState(AudioCaptureState state)
        {
            if (ReferenceEquals(state, _source.State) && !ReferenceEquals(state, _session))
                lock (_gate)
                    Fail("音频源已断开或重新连接；请重新运行脚本。");
        }

        private void Fail(string message)
        {
            _error ??= message;
            _packets.Clear();
            _bytes = 0;
        }

        private void OnData(AudioChunk chunk)
        {
            AudioCaptureState state = _source.State;
            lock (_gate)
            {
                if (_disposed || _error != null)
                    return;
                if (!ReferenceEquals(state, _session))
                {
                    Fail("音频源会话已变化；请重新运行脚本。");
                    return;
                }
                if (chunk.Session == null)
                {
                    Fail("音频源没有提供可验证的会话标识。");
                    return;
                }
                // Ignore a delayed callback from before this run or an older device session.
                if (!ReferenceEquals(chunk.Session, _session) || chunk.TimestampNs <= Start)
                    return;
                long count = chunk.BytesRecorded / _alignment;
                if (chunk.Format != Format || chunk.BytesRecorded <= 0 || chunk.BytesRecorded > chunk.Pcm.Length
                    || chunk.BytesRecorded % _alignment != 0 || chunk.BytesRecorded > MaximumBytes
                    || count * 1_000_000_000L / Format.SampleRate > ToleranceNs)
                {
                    Fail("音频块无效、格式变化或单块超过 250 毫秒。");
                    return;
                }
                long received = _clock();
                if (chunk.TimestampNs > received || received - chunk.TimestampNs > ToleranceNs
                    || (_lastArrival != 0 && chunk.TimestampNs <= _lastArrival))
                {
                    Fail("音频回调迟到或时间戳不连续。");
                    return;
                }
                _origin ??= chunk.TimestampNs - count * 1_000_000_000L / Format.SampleRate;
                if (Math.Abs(TimeAt(_frames + count) - chunk.TimestampNs) > ToleranceNs
                    || (_lastArrival == 0 && TimeAt(0) - Start > ToleranceNs))
                {
                    Fail("音频采样时钟与回调时间偏差超过 250 毫秒，检测数据不可靠。");
                    return;
                }
                if (_firstArrival == 0)
                    _firstArrival = chunk.TimestampNs;
                _lastArrival = chunk.TimestampNs;
                long skip = Math.Clamp(FrameAt(Start, true) - _frames, 0, count);
                if (skip < count)
                {
                    byte[] copy = chunk.Pcm.Span.Slice((int)skip * _alignment, (int)(count - skip) * _alignment).ToArray();
                    _packets.Enqueue(new Packet(_frames + skip, copy, chunk.TimestampNs));
                    _bytes += copy.Length;
                }
                _frames += count;
                while (_packets.TryPeek(out Packet? first)
                    && (_bytes > MaximumBytes || _packets.Count > 4096
                        || TimeAt(first.Frame + first.Pcm.Length / _alignment) <= received - MaximumLookbackMs * 1_000_000L))
                {
                    _packets.Dequeue();
                    _bytes -= first.Pcm.Length;
                    _evictedEnd = TimeAt(first.Frame + first.Pcm.Length / _alignment);
                }
            }
        }

        public void Check()
        {
            AudioCaptureState state = _source.State;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!ReferenceEquals(state, _session))
                    Fail("音频源已断开或重新连接；请重新运行脚本。");
                if (_clock() - (_lastArrival == 0 ? Start : _lastArrival) > 1_000_000_000L)
                    Fail("音频输入超过 1 秒没有新数据。");
                if (_error != null)
                    throw new InvalidOperationException(_error);
            }
        }

        public Segment[] Read(long start, long end, long arrivalDeadline = long.MaxValue)
        {
            Check();
            lock (_gate)
            {
                if (_evictedEnd > start)
                    throw new InvalidOperationException("请求的音频历史已超过 15 秒、16 MiB 或 4096 块缓存上限。");
                if (!_origin.HasValue)
                    return [];
                List<Segment> result = [];
                long lastEligibleArrival = 0;
                foreach (Packet packet in _packets)
                {
                    if (packet.Arrival >= arrivalDeadline || packet.Arrival > end)
                        continue;
                    lastEligibleArrival = packet.Arrival;
                    long from = Math.Max(packet.Frame, FrameAt(start, true));
                    long to = Math.Min(packet.Frame + packet.Pcm.Length / _alignment, FrameAt(end, false));
                    if (to > from)
                        result.Add(new Segment(packet.Pcm.AsMemory((int)(from - packet.Frame) * _alignment,
                            (int)(to - from) * _alignment), TimeAt(to)));
                }
                if (arrivalDeadline == long.MaxValue && end - lastEligibleArrival > ToleranceNs)
                    throw new InvalidOperationException("AUDIO 最近 250 毫秒没有有效音频，窗口末尾数据不足。");
                return result.ToArray();
            }
        }

        public void RequireCoverage(long deadline)
        {
            Check();
            lock (_gate)
            {
                long lastEligibleArrival = 0;
                foreach (Packet packet in _packets)
                    if (packet.Arrival < deadline)
                        lastEligibleArrival = packet.Arrival;
                if (_firstArrival == 0 || _firstArrival - Start > ToleranceNs || deadline - lastEligibleArrival > ToleranceNs)
                    throw new InvalidOperationException("AUDIO_WAIT 等待窗口内没有持续有效的音频，不能判定为未命中。");
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                _packets.Clear();
                _bytes = 0;
            }
            _source.DataAvailable -= OnData;
            _source.StateChanged -= OnState;
        }
    }
}