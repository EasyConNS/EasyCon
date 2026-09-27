namespace EasyCon.Core.Logging;

/// <summary>
/// 库层诊断日志转发器：Core/Device/Capture 等叶子库不依赖具体日志实现，
/// 通过本静态汇点把诊断交给宿主（GUI 接 LogService、CLI 接 Serilog 文件 logger）。
/// 未接宿主时回落 Debug.WriteLine（Release 编译为 no-op，与既有行为一致）。
/// 宿主应在启动时赋值 Sink；Dispose 时置回 null。
/// </summary>
public static class CoreLog
{
    /// <summary>宿主注入的日志汇点（null = 未接宿主）。必须线程安全。</summary>
    public static Action<string>? Sink;

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message) => Write("ERROR", message);

    static void Write(string level, string message)
    {
        System.Diagnostics.Debug.WriteLine($"[EasyCon:{level}] {message}");
        var sink = Sink;
        try
        {
            sink?.Invoke($"[{level}] {message}");
        }
        catch
        {
            // 日志汇点自身的异常不再反噬库层调用方
        }
    }
}