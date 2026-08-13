using EzCv;

namespace EasyCon.Capture;

/// <summary>
/// 单生产者最新帧存储。生产者线程 Publish 最新帧，消费方 AcquireLatest 获取租约。
/// 热路径无锁：Interlocked.Exchange 发布，Interlocked 引用计数。
/// 每个 Mat 恰好 Dispose 一次：引用计数归零的线程负责释放。
/// 引用计数把「当前帧指针」本身计为一次引用，因此仍被 _current 指向的帧不会
/// 因最后一个租约释放而被提前释放，也避免了 -1 终态导致的 AcquireLatest 空转。
/// </summary>
public sealed class FrameStore
{
    internal sealed class FrameSlot
    {
        public readonly Mat Mat;
        public int Ref;   // = 当前指针引用(0/1) + 活动租约数；归零即释放
        public FrameSlot(Mat mat) => Mat = mat;
    }

    private FrameSlot? _current;

    public void Publish(Mat mat)
    {
        var slot = new FrameSlot(mat) { Ref = 1 };
        var old = Interlocked.Exchange(ref _current, slot);
        if (old != null) Drop(old);
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
            s.Mat.Dispose();
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
                return new FrameLease(this, slot);
        }
    }

    internal void Release(FrameSlot slot)
    {
        if (Interlocked.Decrement(ref slot.Ref) == 0)
            slot.Mat.Dispose();
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

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0)
            return;
        _store.Release(_slot);
        GC.SuppressFinalize(this);
    }
}