using Avalonia.Threading;
using EasyCon.Core.Services;

namespace EasyCon2.Avalonia.Core.Services;

public class LogService : ILogService
{
    private readonly List<(string text, string? color)> _entries = new();
    private readonly Timer _flushTimer;
    private readonly object _lock = new();

    public event Action<string?, string?>? LogAppended;

    public LogService()
    {
        _flushTimer = new Timer(Flush, null, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
    }

    public void Print(string message, bool newline)
    {
        var text = newline ? $"[{DateTime.Now:HH:mm:ss}] {message}\n" : message;
        lock (_lock) { _entries.Add((text, null)); }
    }

    public void Alert(string message)
    {
        var text = $"[{DateTime.Now:HH:mm:ss}] [ALERT] {message}\n";
        lock (_lock) { _entries.Add((text, "Orange")); }
    }

    public void AddLog(string message, string? color = null)
    {
        var text = $"[{DateTime.Now:HH:mm:ss}] {message}\n";
        lock (_lock) { _entries.Add((text, color)); }
    }

    public void Clear()
    {
        lock (_lock) { _entries.Clear(); }
        Dispatcher.UIThread.Post(() => LogAppended?.Invoke(null, null));
    }

    private void Flush(object? state)
    {
        (string text, string? color)[] batch;
        lock (_lock)
        {
            if (_entries.Count == 0) return;
            batch = _entries.ToArray();
            _entries.Clear();
        }
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var (text, color) in batch)
                LogAppended?.Invoke(text, color);
        });
    }
}