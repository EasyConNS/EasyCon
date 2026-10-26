namespace EasyCon2.Avalonia.DrawingBoard;

/// <summary>
/// 绘制的暂停开关：界面线程切换，绘制线程在每次按键前检查。
/// 暂停期间仍持有运行租约，脚本与手动输入照样被挡住；停止（取消）会立即打断暂停中的等待。
/// </summary>
public sealed class DrawPause
{
    private readonly ManualResetEventSlim _running = new(true);

    public bool IsPaused => !_running.IsSet;

    public void Pause() => _running.Reset();

    public void Resume() => _running.Set();

    /// <summary>暂停中则阻塞到继续；被取消时抛出 <see cref="OperationCanceledException"/>。</summary>
    internal void WaitWhilePaused(CancellationToken token) => _running.Wait(token);
}