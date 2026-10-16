#nullable enable
using EasyCon.Capture;
using EasyCon.Core.Capabilities;
using OpenCvSharp;
using System.Diagnostics;

namespace EasyCon.Core.Maa;

public interface IFrameHub
{
    bool CaptureConnected { get; }
    bool FrameReady { get; }
    long SourceEpoch { get; }
    FrameLease? AcquireLatest();
    Task<FrameLease> WaitForFrameAfterAsync(long frameId, long capturedAtTicks, CancellationToken token);
}

public enum CaptureFailure { Disconnected, SourceChanged, TimedOut, HostShutdown }

/// <summary>独立采集模块。连接级唯一生产者；重连保留同一个 FrameStore。</summary>
public sealed class FrameHub : IFrameHub, IDisposable
{
    private readonly object _lifecycle = new();
    private readonly object _consumersGate = new();
    private readonly HashSet<CaptureUsageLease> _consumers = new();
    private readonly System.Threading.Timer _health;
    private FrameProducer? _producer;
    private bool _connected;
    private bool _disposed;
    private long _openedAt;

    public FrameHub() => _health = new System.Threading.Timer(_ => CheckHealth(), null, 250, 250);
    public FrameStore Store { get; } = new();
    public TimeSpan FrameTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public bool CaptureConnected => Volatile.Read(ref _connected);
    public bool FrameReady
    {
        get
        {
            using FrameLease? frame = AcquireLatest();
            return frame != null && frame.Metadata.SourceEpoch == SourceEpoch &&
                Stopwatch.GetElapsedTime(frame.Metadata.CapturedAtTicks) <= FrameTimeout;
        }
    }
    public long SourceEpoch => _producer?.SourceEpoch ?? 0;
    public string? SourceId { get; private set; }
    public VideoCaptureAPIs? Backend { get; private set; }
    public CaptureSourceDescriptor? Selection { get; private set; }
    public int? DeviceIndex { get; private set; }
    public bool HeldByUser { get; private set; }
    public bool IsStopping => _producer?.IsStopping == true;
    public string? LastError => _producer?.LastError;
    public int ConsumerCount { get { lock (_consumersGate) return _consumers.Count; } }
    public event Action<CaptureFailure>? Unavailable;
    public FrameLease? AcquireLatest()
    {
        if (!CaptureConnected) return null;
        FrameLease? frame = Store.AcquireLatest();
        if (frame != null && (frame.Metadata.SourceEpoch != SourceEpoch || Stopwatch.GetElapsedTime(frame.Metadata.CapturedAtTicks) > FrameTimeout))
        {
            frame.Dispose();
            return null;
        }
        return frame;
    }
    public Task<FrameLease> WaitForFrameAfterAsync(long frameId, long capturedAtTicks, CancellationToken token)
        => Store.WaitForFrameAfterAsync(frameId, capturedAtTicks, token);

    public CaptureUsageLease AcquireUsage()
    {
        lock (_consumersGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!CaptureConnected) throw new InvalidOperationException("采集源未连接");
            CaptureUsageLease lease = new(this, SourceEpoch);
            _consumers.Add(lease);
            return lease;
        }
    }

    internal void Return(CaptureUsageLease lease)
    {
        lock (_consumersGate) _consumers.Remove(lease);
    }

    private CaptureUsageLease[] CancelConsumers(CaptureFailure reason)
    {
        CaptureUsageLease[] consumers;
        lock (_consumersGate)
        {
            _connected = false;
            consumers = _consumers.ToArray();
        }
        foreach (CaptureUsageLease lease in consumers) lease.Cancel(reason);
        Unavailable?.Invoke(reason);
        return consumers;
    }

    private void DrainConsumers(CaptureFailure reason)
    {
        foreach (CaptureUsageLease lease in CancelConsumers(reason)) lease.Completion.GetAwaiter().GetResult();
    }

    private void CheckHealth()
    {
        if (!CaptureConnected) return;
        try
        {
            using FrameLease? frame = Store.AcquireLatest();
            long arrival = frame?.Metadata.CapturedAtTicks ?? _openedAt;
            if (_producer?.IsOpened == false || Stopwatch.GetElapsedTime(arrival) > FrameTimeout)
                CancelConsumers(CaptureFailure.TimedOut);
        }
        catch (ObjectDisposedException) { }
    }

    public bool Connect(CaptureSourceDescriptor selection, FrameNormalization? normalization = null)
    {
        CaptureSourceDescriptor resolved = CaptureSourceCatalog.Resolve(selection, CaptureSourceCatalog.Enumerate(selection.Backend));
        return ConnectCore(resolved.Index, resolved.Backend, resolved.Id, resolved, normalization);
    }

    /// <summary>CLI 的临时数字索引按明确后端解释，不使用其他后端的枚举映射。</summary>
    public bool Connect(int deviceId, VideoCaptureAPIs api = VideoCaptureAPIs.ANY, FrameNormalization? normalization = null)
    {
        if (deviceId < 0) throw new ArgumentOutOfRangeException(nameof(deviceId));
        VideoCaptureAPIs backend = CaptureSourceCatalog.ResolveBackend(api);
        return ConnectCore(deviceId, backend, $"temporary:{backend}:{deviceId}", null, normalization);
    }

    private bool ConnectCore(int deviceId, VideoCaptureAPIs backend, string sourceId,
        CaptureSourceDescriptor? selection, FrameNormalization? normalization)
    {
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            DrainConsumers(CaptureFailure.SourceChanged);
            StopPrevious();
            SourceOwnership ownership = new($"camera:{deviceId}");
            OpenCVCapture capture = new();
            try
            {
                if (!capture.Open(deviceId, (int)backend))
                {
                    capture.Dispose(); ownership.Dispose();
                    return false;
                }
                if (capture.Backend != backend) throw new IOException($"采集实际后端 {capture.Backend} 与选择 {backend} 不符");
                capture.SetProperties(FrameNormalization.Width, FrameNormalization.Height);
                _producer = new FrameProducer(new OwnedSource(capture, ownership), Store, normalization, sourceId);
                _producer.Start();
                SourceId = sourceId;
                Backend = backend;
                Selection = selection;
                DeviceIndex = deviceId;
                _openedAt = Stopwatch.GetTimestamp();
                HeldByUser = true;
                _connected = true;
                return true;
            }
            catch { capture.Dispose(); ownership.Dispose(); throw; }
        }
    }

    internal void Connect(IFrameSource source, string sourceId, FrameNormalization? normalization = null)
    {
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            DrainConsumers(CaptureFailure.SourceChanged);
            StopPrevious();
            _producer = new FrameProducer(source, Store, normalization, sourceId);
            _producer.Start();
            SourceId = sourceId;
            _openedAt = Stopwatch.GetTimestamp();
            HeldByUser = true;
            _connected = true;
        }
    }

    private void StopPrevious()
    {
        if (_producer == null) return;
        if (!_producer.Stop()) throw new InvalidOperationException("busy: 旧采集 Read 尚未退出，仍在停止中");
        _producer.Dispose();
        _producer = null;
    }

    public bool Disconnect()
    {
        lock (_lifecycle)
        {
            HeldByUser = false;
            DrainConsumers(CaptureFailure.Disconnected);
            if (_producer == null) return true;
            if (!_producer.Stop()) return false;
            _producer.Dispose();
            _producer = null;
            return true;
        }
    }

    public void Dispose()
    {
        lock (_lifecycle)
        {
            if (_disposed) return;
            DrainConsumers(CaptureFailure.HostShutdown);
            _disposed = true;
            _health.Dispose();
            _producer?.Dispose();
            Store.ReleaseCurrent();
        }
    }

    private sealed class OwnedSource(IFrameSource source, SourceOwnership ownership) : IFrameSource
    {
        public bool IsOpened => source.IsOpened;
        public Mat GetMatFrame() => source.GetMatFrame();
        public void SetProperties(int width, int height) => source.SetProperties(width, height);
        public void Dispose()
        {
            try { source.Dispose(); }
            finally { ownership.Dispose(); }
        }
    }
}

/// <summary>Flow 等端口也经过帧门面的代次与新鲜度检查；不另读全局帧号作为图像身份。</summary>
public sealed class FrameHubCaptureSource(FrameHub hub) : ICaptureSource
{
    public long? FrameIndex => hub.Store.FrameCount;
    public string? CaptureFrame(int x, int y, int width, int height)
    {
        using FrameLease? frame = hub.AcquireLatest();
        if (frame == null) return null;
        if (x < 0 || y < 0 || width < 0 || height < 0) return Convert.ToBase64String(frame.EncodedPng.Span);
        return FrameDelegateFactory.CropToBase64(frame.Mat, x, y, width, height);
    }
}

public sealed class CaptureUsageLease : IDisposable
{
    private readonly FrameHub _hub;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _returned;
    internal CaptureUsageLease(FrameHub hub, long epoch)
    {
        _hub = hub;
        SourceEpoch = epoch;
        Token = _cancellation.Token;
    }
    public long SourceEpoch { get; }
    public CancellationToken Token { get; }
    public CaptureFailure? Failure { get; private set; }
    public Task Completion => _completion.Task;
    internal void Cancel(CaptureFailure reason)
    {
        if (Volatile.Read(ref _returned) != 0) return;
        Failure ??= reason;
        try { _cancellation.Cancel(); }
        catch (ObjectDisposedException) { }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _returned, 1) != 0) return;
        _hub.Return(this);
        _completion.TrySetResult();
        _cancellation.Dispose();
    }
}