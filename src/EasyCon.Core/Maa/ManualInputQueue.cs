#nullable enable
using EasyCon.Core.Logging;
using System.Threading.Channels;

namespace EasyCon.Core.Maa;

/// <summary>手动输入接受后在后台串行等待 Maa；绑定身份随入队固定，UI/SDL 不等待原生作业。</summary>
internal sealed class ManualInputQueue : IDisposable
{
    private sealed record Item(MaaControllerBinding? Binding, ControllerCommand? Command, TaskCompletionSource? Barrier = null);
    private readonly Channel<Item> _queue = Channel.CreateBounded<Item>(new BoundedChannelOptions(256)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait,
    });
    private readonly Task _worker;
    private int _overflow;
    public ManualInputQueue() => _worker = Task.Run(Consume);

    public void Post(MaaControllerBinding binding, ControllerCommand command)
    {
        if (_queue.Writer.TryWrite(new(binding, command))) return;
        Interlocked.Exchange(ref _overflow, 1);
        CoreLog.Warn("手动输入队列已满；将丢弃待处理输入并确认中性报告");
    }

    public void Drain()
    {
        TaskCompletionSource barrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Writer.WriteAsync(new(null, null, barrier)).AsTask().GetAwaiter().GetResult();
        barrier.Task.GetAwaiter().GetResult();
    }

    private async Task Consume()
    {
        await foreach (Item item in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (Interlocked.Exchange(ref _overflow, 0) != 0)
            {
                List<TaskCompletionSource> barriers = [];
                MaaControllerBinding? binding = item.Binding;
                if (item.Barrier != null) barriers.Add(item.Barrier);
                while (_queue.Reader.TryRead(out Item? discarded))
                {
                    binding ??= discarded.Binding;
                    if (discarded.Barrier != null) barriers.Add(discarded.Barrier);
                }
                try { binding?.ReleaseInput(); }
                catch (Exception ex) { CoreLog.Warn($"手动输入队列中性报告失败: {ex.Message}"); }
                foreach (TaskCompletionSource barrier in barriers) barrier.TrySetResult();
                continue;
            }
            if (item.Barrier != null) { item.Barrier.TrySetResult(); continue; }
            try { item.Binding!.Send(item.Command!, default); }
            catch (Exception ex) { CoreLog.Warn($"手动输入被拒绝: {ex.Message}"); }
        }
    }

    public void Dispose()
    {
        _queue.Writer.TryComplete();
        _worker.GetAwaiter().GetResult();
    }
}