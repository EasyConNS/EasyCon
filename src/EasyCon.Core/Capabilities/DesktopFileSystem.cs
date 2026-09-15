using System.Collections.Concurrent;
using System.Text;

namespace EasyCon.Core.Capabilities;

/// <summary>
/// 桌面参考文件实现（.NET 文件系统 + 句柄表；自 BuiltinCallable 平移，语义保留）。
/// 句柄 &gt;= 3；0=stdin/1=stdout/2=stderr 的行断协议由 VM 参考语义（ReferenceSyscall）
/// 承担，本实现仅保留其防御分支。失败语义：返回约定哨兵值，不抛异常。
/// </summary>
public sealed class DesktopFileSystem : IFileSystem
{
    public static readonly DesktopFileSystem Instance = new();

    readonly ConcurrentDictionary<long, StreamReader> _readers = new();
    readonly ConcurrentDictionary<long, StreamWriter> _writers = new();
    long _nextHandle = 3;

    DesktopFileSystem()
    {
    }

    /// <summary>关闭所有打开的文件句柄（每次脚本运行结束时调用）。</summary>
    public void CloseAllFiles()
    {
        foreach (var kvp in _readers)
        {
            try { kvp.Value.Dispose(); }
            catch { }
        }
        foreach (var kvp in _writers)
        {
            try { kvp.Value.Dispose(); }
            catch { }
        }
        _readers.Clear();
        _writers.Clear();
        _nextHandle = 3;
    }

    public long Open(string path, string mode)
    {
        try
        {
            long handle = Interlocked.Increment(ref _nextHandle) - 1;
            switch (mode)
            {
                case "r":
                    _readers[handle] = new StreamReader(path, Encoding.UTF8);
                    break;
                case "w":
                    _writers[handle] = new StreamWriter(path, false, Encoding.UTF8);
                    break;
                case "a":
                    _writers[handle] = new StreamWriter(path, true, Encoding.UTF8);
                    break;
                default:
                    return -1;
            }
            return handle;
        }
        catch
        {
            return -1;
        }
    }

    public string Read(long handle, int count)
    {
        // 标准输入（ReferenceSyscall 已先行处理；防御分支与原 ImplFRead 一致）
        if (handle == 0)
            return Console.ReadLine() ?? "";

        if (!_readers.TryGetValue(handle, out var reader))
            return "";

        try
        {
            if (count <= 0)
                return reader.ReadToEnd();

            var buffer = new char[count];
            int read = reader.Read(buffer, 0, count);
            return new string(buffer, 0, read);
        }
        catch
        {
            return "";
        }
    }

    public int Write(long handle, string data)
    {
        // 标准句柄不落此处（ReferenceSyscall 行断协议承担）；防御返回写入长度
        if (handle is >= 0 and <= 2)
            return data.Length;

        if (!_writers.TryGetValue(handle, out var writer))
            return -1;

        try
        {
            writer.Write(data);
            return data.Length;
        }
        catch
        {
            return -1;
        }
    }

    public void Close(long handle)
    {
        if (handle < 3)
            return;

        if (_readers.TryRemove(handle, out var reader))
        {
            try { reader.Dispose(); }
            catch { }
        }
        if (_writers.TryRemove(handle, out var writer))
        {
            try { writer.Dispose(); }
            catch { }
        }
    }

    public bool Eof(long handle)
    {
        if (handle == 0)
            return false;
        if (!_readers.TryGetValue(handle, out var reader))
            return true;
        try { return reader.Peek() == -1; }
        catch { return true; }
    }

    public string ReadAllText(string path)
    {
        try { return File.ReadAllText(path); }
        catch { return ""; }
    }

    public void WriteAllText(string path, string content)
    {
        try { File.WriteAllText(path, content); }
        catch { }
    }

    public void AppendAllText(string path, string content)
    {
        try { File.AppendAllText(path, content); }
        catch { }
    }

    public bool Exists(string path) => File.Exists(path);
}