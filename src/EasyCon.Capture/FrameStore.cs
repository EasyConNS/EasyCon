using OpenCvSharp;

namespace EasyCon.Capture;

/// <summary>
/// 单生产者最新帧存储。生产者线程 Publish 最新帧，消费方 AcquireLatest 获取租约。
/// AcquireLatest 无锁；发布与新帧通知在同一短锁内完成，租约使用原子引用计数。
/// 每个 Mat 恰好 Dispose 一次：引用计数归零的线程负责释放。
/// 引用计数把「当前帧指针」本身计为一次引用，因此仍被 _current 指向的帧不会
/// 因最后一个租约释放而被提前释放，也避免了 -1 终态导致的 AcquireLatest 空转。
/// </summary>
public sealed class FrameStore
{
    private static int _activeSlots;
    private static int _activeLeases;
    public static int ActiveSlots => Volatile.Read(ref _activeSlots);
    public static int ActiveLeases => Volatile.Read(ref _activeLeases);
    internal sealed class FrameSlot
    {
        public readonly Mat Mat;
        public readonly FrameMetadata Metadata;
        private readonly Lazy<byte[]> _png;
        public int Ref;   // = 当前指针引用(0/1) + 活动租约数；归零即释放
        public FrameSlot(Mat mat, FrameMetadata metadata)
        {
            Mat = mat;
            Metadata = metadata;
            _png = new Lazy<byte[]>(() => mat.ToBytes(".png"));
            Interlocked.Increment(ref _activeSlots);
        }
        public ReadOnlyMemory<byte> Png => _png.Value;
    }

    private FrameSlot? _current;
    private long _frameCount;
    private long _sourceEpoch;
    private readonly object _publishGate = new();
    private TaskCompletionSource _published = NewSignal();

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public long BeginSource() => Interlocked.Increment(ref _sourceEpoch);

    /// <summary>累计发布帧数（单调递增，供「等待新帧」与慢感知 everyFrames 判定）。</summary>
    public long FrameCount => Interlocked.Read(ref _frameCount);

    public void Publish(Mat mat) => Publish(mat, FrameMetadata.FromImage(mat));

    public void Publish(Mat mat, FrameMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(mat);
        ArgumentNullException.ThrowIfNull(metadata);
        FrameSlot? old;
        TaskCompletionSource published;
        lock (_publishGate)
        {
            long id = Interlocked.Increment(ref _frameCount);
            FrameSlot slot = new(mat, metadata with { FrameId = id }) { Ref = 1 };
            old = Interlocked.Exchange(ref _current, slot);
            published = _published;
            _published = NewSignal();
        }
        published.TrySetResult();
        if (old != null) Drop(old);
    }

    /// <summary>等待真正更新且晚于指定宿主到达时间的帧；不触发采集。</summary>
    public async Task<FrameLease> WaitForFrameAfterAsync(long frameId, long capturedAtTicks,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Task next;
            lock (_publishGate)
            {
                FrameLease? lease = AcquireLatest();
                if (lease != null)
                {
                    if (lease.Metadata.FrameId > frameId && lease.Metadata.CapturedAtTicks > capturedAtTicks)
                        return lease;
                    lease.Dispose();
                }
                next = _published.Task;
            }
            await next.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 清空当前帧（停止采集时调用），与 Publish 退役旧帧走同一路径。
    /// </summary>
    public void ReleaseCurrent()
    {
        var old = Interlocked.Exchange(ref _current, null);
        if (old != null) Drop(old);
    }

    private static void Drop(FrameSlot s)
    {
        if (Interlocked.Decrement(ref s.Ref) == 0)
        {
            s.Mat.Dispose();
            Interlocked.Decrement(ref _activeSlots);
        }
    }

    public FrameLease? AcquireLatest()
    {
        while (true)
        {
            var slot = Volatile.Read(ref _current);
            if (slot == null) return null;
            var r = Volatile.Read(ref slot.Ref);
            if (r <= 0) continue;  // 已被退役/释放，取更新的
            if (Interlocked.CompareExchange(ref slot.Ref, r + 1, r) == r)
            {
                Interlocked.Increment(ref _activeLeases);
                return new FrameLease(this, slot);
            }
        }
    }

    internal void Release(FrameSlot slot)
    {
        Interlocked.Decrement(ref _activeLeases);
        if (Interlocked.Decrement(ref slot.Ref) == 0)
        {
            slot.Mat.Dispose();
            Interlocked.Decrement(ref _activeSlots);
        }
    }
}

/// <summary>
/// 最新帧租约。持有租约期间可安全使用共享 Mat（含由它派生的 ROI 视图）。
/// Dispose 幂等，且必须持有到不再使用 Mat 为止。
/// </summary>
public sealed class FrameLease : IDisposable
{
    private readonly FrameStore _store;
    private readonly FrameStore.FrameSlot _slot;
    private int _released;

    internal FrameLease(FrameStore store, FrameStore.FrameSlot slot)
    {
        _store = store;
        _slot = slot;
    }

    /// <summary>共享的最新帧（非克隆）。租约存续期间有效。</summary>
    public Mat Mat => _slot.Mat;
    public FrameMetadata Metadata => _slot.Metadata;
    public ReadOnlyMemory<byte> EncodedPng => _slot.Png;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0)
            return;
        _store.Release(_slot);
        GC.SuppressFinalize(this);
    }
}