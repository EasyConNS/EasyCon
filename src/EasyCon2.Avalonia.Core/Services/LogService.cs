using Avalonia.Threading;
using EasyCon.Core.Services;
using EasyScript;
using Serilog;

namespace EasyCon2.Avalonia.Core.Services;

public class LogService : ILogService, IDisposable
{
    private readonly List<(string text, string? color)> _entries = new();
    private readonly Timer _flushTimer;
    private readonly object _lock = new();
    private readonly string _logDirectory;
    private readonly ILogger _fileLogger;

    public event Action<string?, string?>? LogAppended;

    public LogService()
    {
        _flushTimer = new Timer(Flush, null, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
        _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        Directory.CreateDirectory(_logDirectory);
        _fileLogger = new LoggerConfiguration()
            .WriteTo.File(
                Path.Combine(_logDirectory, "easycon-.log"),
                rollingInterval: RollingInterval.Day,
                rollOnFileSizeLimit: true,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                retainedFileCountLimit: null,
                shared: false,
                buffered: true,
                flushToDiskInterval: TimeSpan.FromSeconds(2),
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}")
            .CreateLogger();
    }

    public void Print(string message, bool newline)
    {
        var text = newline ? $"[{DateTime.Now:HH:mm:ss}] {message}\n" : message;
        _fileLogger.Information(message);
        lock (_lock) { _entries.Add((text, null)); }
    }

    public void Alert(string message)
    {
        var text = $"[{DateTime.Now:HH:mm:ss}] [ALERT] {message}\n";
        _fileLogger.Warning(message);
        lock (_lock) { _entries.Add((text, "Orange")); }
    }

    public string ReadLine()
    {
        throw new NotImplementedException("ReadLine is not implemented in LogService");
    }

    public bool TryReadLine(out string line)
    {
        throw new NotImplementedException("TryReadLine is not implemented in LogService");
    }

    public void AddLog(string message, string? color = null)
    {
        var text = $"[{DateTime.Now:HH:mm:ss}] {message}\n";
        _fileLogger.Information(message);
        lock (_lock) { _entries.Add((text, color)); }
    }

    public void Dispose()
    {
        _flushTimer?.Dispose();
        (_fileLogger as IDisposable)?.Dispose();
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

        // 高频打印保护：UI 每次只消费最近的一批（完整记录已由 Serilog 落盘），
        // 避免海量行一次性推给 UI 线程导致卡死。被跳过的旧行仅不再展示，文件仍完整。
        const int MaxUiBatch = 500;
        if (batch.Length > MaxUiBatch)
            batch = batch[^MaxUiBatch..];

        Dispatcher.UIThread.Post(() =>
        {
            foreach (var (text, color) in batch)
                LogAppended?.Invoke(text, color);
        });
    }
}