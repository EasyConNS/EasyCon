using Avalonia.Threading;
using EasyScript;

namespace EC.Avalonia.Services;

public class LogService : ILogService
{
    public event Action<string>? LogAppended;

    public void Print(string message, bool newline)
    {
        var text = newline ? $"[{DateTime.Now:HH:mm:ss}] {message}\n" : message;
        Dispatcher.UIThread.Post(() => LogAppended?.Invoke(text));
    }

    public void Alert(string message)
    {
        var text = $"[{DateTime.Now:HH:mm:ss}] [ALERT] {message}\n";
        Dispatcher.UIThread.Post(() => LogAppended?.Invoke(text));
    }

    public void AddLog(string message)
    {
        var text = $"[{DateTime.Now:HH:mm:ss}] {message}\n";
        Dispatcher.UIThread.Post(() => LogAppended?.Invoke(text));
    }

    public void Clear()
    {
        LogAppended?.Invoke(null);
    }
}
