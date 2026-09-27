using Avalonia.Threading;
using EasyCon2.Avalonia.Core.Threading;

namespace EasyCon2.Avalonia.Services;

/// <summary>
/// <see cref="IUiDispatcher"/> 的 Avalonia 实现，App 启动时注入到 Core 层服务。
/// </summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public static AvaloniaUiDispatcher Instance { get; } = new();

    public bool IsOnUiThread => Dispatcher.UIThread.CheckAccess();

    public void Post(Action action) => Dispatcher.UIThread.Post(action);

    public Task InvokeAsync(Action action) => Dispatcher.UIThread.InvokeAsync(action).GetTask();

    public Task InvokeAsync(Func<Task> action) => Dispatcher.UIThread.InvokeAsync(action);
}