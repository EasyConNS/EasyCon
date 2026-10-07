namespace EasyCon2.Avalonia.Services;

/// <summary>
/// UI 线程调度抽象。Core 层的 VM/服务通过本接口把工作派发到 UI 线程，
/// 而不直接引用具体 UI 框架的 Dispatcher；GUI 宿主启动时注入实现，
/// 测试可注入 <see cref="SynchronousUiDispatcher"/>。
/// </summary>
public interface IUiDispatcher
{
    /// <summary>当前是否已在 UI 线程上。</summary>
    bool IsOnUiThread { get; }

    /// <summary>
    /// 把操作调度到 UI 线程异步执行：不阻塞调用线程，也不等待其完成。
    /// </summary>
    void Post(Action action);

    /// <summary>在 UI 线程执行操作并等待完成。</summary>
    Task InvokeAsync(Action action);

    /// <summary>在 UI 线程执行异步操作并等待完成。</summary>
    Task InvokeAsync(Func<Task> action);
}

/// <summary>测试/设计器回退实现：所有调用在当前线程内联执行。</summary>
public sealed class SynchronousUiDispatcher : IUiDispatcher
{
    public static readonly SynchronousUiDispatcher Instance = new();

    public bool IsOnUiThread => true;

    public void Post(Action action) => action();

    public Task InvokeAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    public Task InvokeAsync(Func<Task> action) => action();
}