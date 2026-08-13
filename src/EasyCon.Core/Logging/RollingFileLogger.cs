using System.Collections.Concurrent;
using System.Text;

namespace EasyCon.Core.Logging;

/// <summary>
/// 滚动文件日志器：按日期 + 文件大小切分日志文件，写入到可执行目录旁的 logs/ 文件夹。
/// 内部使用单后台写线程 + 无锁并发队列，任何生产者线程都可直接 Enqueue，无需额外加锁。
/// </summary>
public sealed class RollingFileLogger : IDisposable
{
    private readonly string _logDirectory;
    private readonly long _maxFileSizeBytes;
    private readonly int _newLineBytes;
    private readonly ConcurrentQueue<string> _queue = new();
    private readonly AutoResetEvent _newLine = new(false);
    private readonly ManualResetEventSlim _flushed = new(true);
    private readonly object _flushLock = new();
    private readonly Thread _writerThread;

    private volatile bool _disposed;
    private volatile bool _flushRequested;

    private StreamWriter? _writer;
    private long _currentBytes;
    private string _currentDay = "";
    private int _currentSequence;

    /// <summary>
    /// 创建滚动文件日志器。
    /// </summary>
    /// <param name="logDirectory">日志目录，默认取可执行目录旁的 logs/。</param>
    /// <param name="maxFileSizeBytes">单个日志文件的最大字节数，超过后按序号切分。</param>
    public RollingFileLogger(string? logDirectory = null, long maxFileSizeBytes = 10 * 1024 * 1024)
    {
        _logDirectory = logDirectory ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        _maxFileSizeBytes = maxFileSizeBytes;
        _newLineBytes = Encoding.UTF8.GetByteCount(Environment.NewLine);
        Directory.CreateDirectory(_logDirectory);

        _writerThread = new Thread(WriterLoop)
        {
            IsBackground = true,
            Name = "RollingFileLogger",
        };
        _writerThread.Start();
    }

    /// <summary>
    /// 写入一行日志（线程安全）。写线程会自动补全完整时间戳与换行。
    /// 释放（Dispose）之后调用为 no-op。
    /// </summary>
    public void WriteLine(string message)
    {
        if (_disposed)
            return;
        _queue.Enqueue(message);
        _newLine.Set();
    }

    /// <summary>
    /// 强制同步排空队列并刷新文件。返回时所有已入队的行均已落盘。
    /// </summary>
    public void Flush()
    {
        if (_disposed)
            return;
        lock (_flushLock)
        {
            if (_disposed)
                return;
            _flushed.Reset();
            _flushRequested = true;
            _newLine.Set();
            _flushed.Wait();
        }
    }

    /// <summary>
    /// 停止写线程，排空剩余队列并关闭当前文件。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _newLine.Set();
        _writerThread.Join();
        _flushed.Dispose();
        _newLine.Dispose();
    }

    private void WriterLoop()
    {
        while (true)
        {
            bool wroteAny = false;
            while (_queue.TryDequeue(out var line))
            {
                WriteLineCore(line);
                wroteAny = true;
            }

            if (wroteAny)
                _writer?.Flush();

            // 处理 Flush 请求：确保数据落盘后再唤醒等待方
            if (_flushRequested)
            {
                _writer?.Flush();
                _flushRequested = false;
                _flushed.Set();
            }

            if (_disposed)
                break;

            // 最多等待 500ms，然后冲刷缓冲
            _newLine.Reset();
            if (!_queue.IsEmpty)
                continue;
            _newLine.WaitOne(500);
        }

        // 收尾：排空（理论上已空）、刷新并关闭文件
        _writer?.Flush();
        _writer?.Dispose();
        _writer = null;
    }

    private void WriteLineCore(string message)
    {
        var now = DateTime.Now;
        var day = now.ToString("yyyyMMdd");

        // 日期切换：回落到无后缀的当日文件，并重置序号
        if (day != _currentDay)
        {
            _currentDay = day;
            _currentSequence = 0;
            OpenCurrentFile(day);
        }
        else if (_writer == null)
        {
            OpenCurrentFile(day);
        }

        var line = $"[{now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
        long lineBytes = Encoding.UTF8.GetByteCount(line) + _newLineBytes;

        // 尺寸超限：滚动到下一个序号文件（空文件不滚动，避免单行过大时死循环）
        if (_currentBytes > 0 && _currentBytes + lineBytes > _maxFileSizeBytes)
        {
            _currentSequence++;
            OpenCurrentFile(day);
        }

        _writer!.Write(line);
        _writer.Write(Environment.NewLine);
        _currentBytes += lineBytes;
    }

    private void OpenCurrentFile(string day)
    {
        _writer?.Flush();
        _writer?.Dispose();

        var fileName = _currentSequence == 0
            ? $"easycon_{day}.log"
            : $"easycon_{day}_{_currentSequence:D3}.log";
        var path = Path.Combine(_logDirectory, fileName);

        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        _writer.AutoFlush = false;
        _currentBytes = stream.Length;
    }
}
