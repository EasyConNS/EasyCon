using EasyCon.Core.Capabilities;
using EasyCon.Core.Maa;

namespace EasyCon2.Avalonia.DrawingBoard;

/// <summary>画板占用手柄的方式。</summary>
public interface IDrawingRunner
{
    bool IsConnected { get; }

    /// <summary>
    /// 独占手柄执行 <paramref name="draw"/>，回调拿到租约下的手柄输入与运行令牌。
    /// 结束（画完、取消或出错）时由租约写出中性报告；手柄已被占用时抛 <see cref="ControllerOperationException"/>（Busy）。
    /// </summary>
    void Run(Action<IPadInput, CancellationToken> draw, CancellationToken token);
}

/// <summary>
/// 与脚本运行相同的独占机制：经 <see cref="HostRunCoordinator"/> 取得运行租约。
/// 画图期间脚本运行、虚拟手柄与键盘映射等手动输入都会被拒绝，避免误触把画弄歪。
/// </summary>
public sealed class DeviceDrawingRunner(IControllerSession controller) : IDrawingRunner
{
    private readonly HostRunCoordinator _runs = new(controller);

    public bool IsConnected => controller.InputConnected;

    public void Run(Action<IPadInput, CancellationToken> draw, CancellationToken token)
        => _runs.Run("drawing-board", null, requiresInput: true, token, scope => draw(scope.CreatePad(), scope.Token));
}