using Avalonia.Threading;
using EasyScript;
using System.Text;

namespace EasyCon2.Avalonia.Services;

public class LogService : ILogService
{
    private readonly StringBuilder _buffer = new();
    private readonly Timer _flushTimer;
    private readonly object _lock = new();

    public event Action<string>? LogAppended;

    public LogService()
    {
        _flushTimer = new Timer(Flush, null, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
    }

    public void Print(string message, bool newline)
    {
        var text = newline ? $"[{DateTime.Now:HH:mm:ss}] {message}\n" : message;
        Append(text);
    }

    public void Alert(string message)
    {
        var text = $"[{DateTime.Now:HH:mm:ss}] [ALERT] {message}\n";
        Append(text);
    }

    public void AddLog(string message)
    {
        var text = $"[{DateTime.Now:HH:mm:ss}] {message}\n";
        Append(text);
    }

    public void Clear()
    {
        lock (_lock)
        {
            _buffer.Clear();
        }
        LogAppended?.Invoke(null);
    }

    private void Append(string text)
    {
        lock (_lock)
        {
            _buffer.Append(text);
        }
    }

    private void Flush(object? state)
    {
        string chunk;
        lock (_lock)
        {
            if (_buffer.Length == 0) return;
            chunk = _buffer.ToString();
            _buffer.Clear();
        }
        Dispatcher.UIThread.Post(() => LogAppended?.Invoke(chunk));
    }
}