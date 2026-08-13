using EasyCon.Capture;
using EzCv;

namespace EasyCon.Tests;

/// <summary>
/// FrameStore 并发正确性回归：验证单生产者/多消费者的最新帧协议
/// （无异常、无崩溃、无 use-after-free、租约可幂等释放）。
/// </summary>
[TestFixture]
public class FrameStoreTests
{
    [Test]
    public void AcquireLatest_ReturnsNull_BeforeFirstPublish()
    {
        var store = new FrameStore();
        Assert.That(store.AcquireLatest(), Is.Null);
        store.ReleaseCurrent();
    }

    [Test]
    public void Dispose_Lease_Twice_IsSafe()
    {
        var store = new FrameStore();
        store.Publish(new Mat());

        var lease = store.AcquireLatest();
        Assert.That(lease, Is.Not.Null);
        Assert.That(lease!.Mat, Is.Not.Null);

        lease.Dispose();
        lease.Dispose(); // 幂等，不应抛出

        // 释放租约后当前帧仍在（引用计数含当前指针引用），可再次获取且无空转。
        using var again = store.AcquireLatest();
        Assert.That(again, Is.Not.Null);

        store.ReleaseCurrent();
    }

    [Test]
    public void Concurrent_Acquire_And_Publish_NoCrash()
    {
        const int publishCount = 2000;
        const int consumerCount = 8;

        var store = new FrameStore();
        using var start = new ManualResetEventSlim(false);

        var producer = Task.Run(() =>
        {
            start.Wait();
            for (int i = 0; i < publishCount; i++)
                store.Publish(new Mat());
        });

        var consumers = new Task[consumerCount];
        for (int c = 0; c < consumerCount; c++)
        {
            consumers[c] = Task.Run(() =>
            {
                start.Wait();
                int acquired = 0;
                while (acquired < publishCount)
                {
                    using var lease = store.AcquireLatest();
                    if (lease == null) continue;
                    // 触发 native 访问，验证租约持有期间无 use-after-free
                    _ = lease.Mat.Width;
                    _ = lease.Mat.Height;
                    acquired++;
                }
            });
        }

        start.Set();
        Task.WaitAll(consumers);
        producer.Wait();

        store.ReleaseCurrent();
    }
}