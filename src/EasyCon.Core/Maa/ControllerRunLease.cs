#nullable enable
using EasyCon.Core.Input;

namespace EasyCon.Core.Maa;

/// <summary>一次设备写运行。取消立即到达回调；完成清理后才归还代次。</summary>
public sealed class ControllerRunLease : IDisposable
{
    private readonly object _gate = new();
    private Task<CleanupStatus>? _completion;
    private readonly TaskCompletionSource<CleanupStatus> _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal MaaControllerSession Owner { get; }
    internal InputOwner InputOwner { get; }
    internal MaaControllerBinding Binding { get; }
    public string RunId { get; }
    public long Epoch { get; }
    public CancellationToken Token => InputOwner.Token;
    public RunCancellationReason? CancellationReason => InputOwner.Reason;
    public CleanupStatus Cleanup { get; private set; } = CleanupStatus.NotRequired;
    public Task<CleanupStatus> Completion => _finished.Task;

    internal ControllerRunLease(MaaControllerSession owner, InputOwner inputOwner, MaaControllerBinding binding)
    {
        Owner = owner;
        InputOwner = inputOwner;
        Binding = binding;
        RunId = inputOwner.RunId!;
        Epoch = inputOwner.Epoch;
    }

    public void Cancel(RunCancellationReason reason = RunCancellationReason.UserStop)
    {
        if (!Completion.IsCompleted) InputOwner.Cancel(reason);
    }

    public Task<CleanupStatus> StopAsync()
    {
        Cancel();
        lock (_gate) return _completion ??= Task.Run(() =>
        {
            try
            {
                Cleanup = Owner.EndRun(this);
                _finished.TrySetResult(Cleanup);
                return Cleanup;
            }
            catch (Exception ex) { _finished.TrySetException(ex); throw; }
        });
    }

    public void Dispose() => StopAsync().GetAwaiter().GetResult();
}