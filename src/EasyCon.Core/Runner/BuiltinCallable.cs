using EasyCon.Script.Binding;
using EasyCon.Script.Runtime;
using EasyCon.Script.Symbols;
using EasyScript;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using System.Text.Json;

namespace EasyCon.Core.Runner;

/// <summary>
/// 内置函数 Callable 实现。每个静态方法对应一个内置函数的执行逻辑。
/// </summary>
internal static class BuiltinCallable
{
    // ---- 文件句柄表（静态，跨 evaluator 共享） ----
    // 标准句柄：0=stdin, 1=stdout, 2=stderr
    private static readonly ConcurrentDictionary<long, StreamReader> _readers = new();
    private static readonly ConcurrentDictionary<long, StreamWriter> _writers = new();
    private static long _nextHandle = 3;

    /// <summary>关闭所有打开的文件句柄（evaluator 结束时调用）。</summary>
    public static void CloseAllFiles()
    {
        foreach (var kvp in _readers) { try { kvp.Value.Dispose(); } catch { } }
        foreach (var kvp in _writers) { try { kvp.Value.Dispose(); } catch { } }
        _readers.Clear();
        _writers.Clear();
        _nextHandle = 3;
    }

    public static Value ImplAlert(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        var s = args[0].AsString();
        var output = s.EndsWith('\\') ? s[..^1] : s;
        ctx.IoAdapter?.Alert(output);
        return Value.Void;
    }

    public static Value ImplAmiibo(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        var index = args[0].AsInt();
        if (index > 9) return Value.Void;
        ctx.GamePad?.ChangeAmiibo((uint)index);
        return Value.Void;
    }

    public static Value ImplBeep(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        var freq = args[0].AsInt();
        if (freq < 37 || freq > 32767) throw new Exception("BEEP参数freq范围不正确(37~32767)");
        Console.Beep(freq, args[1].AsInt());
        return Value.Void;
    }

    public static Value ImplJq(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        try
        {
            var json = JsonDocument.Parse(args[0].AsString()).RootElement;
            var query = args[1].AsString();

            var current = json;
            var i = 0;
            while (i < query.Length)
            {
                if (query[i] == '.')
                {
                    i++;
                    var start = i;
                    while (i < query.Length && query[i] != '.' && query[i] != '[')
                        i++;
                    current = current.GetProperty(query[start..i]);
                }
                else if (query[i] == '[')
                {
                    i++;
                    var start = i;
                    while (query[i] != ']')
                        i++;
                    current = current[int.Parse(query[start..i])];
                    i++;
                }
                else
                {
                    i++;
                }
            }

            return Value.FromString(current.ValueKind switch
            {
                JsonValueKind.Number => current.GetRawText(),
                JsonValueKind.String => current.GetString()!,
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Array => current.GetRawText(),
                _ => current.GetRawText(),
            });
        }
        catch
        {
            return Value.FromString("");
        }
    }

    public static Value ImplStrEncode(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        var array = args[0].AsArray();
        var bytes = new byte[array.Length];
        for (int i = 0; i < array.Length; i++)
            bytes[i] = array[i].AsByte();
        return args[1].AsString() switch
        {
            "unicode" => Encoding.Unicode.GetString(bytes),
            "utf8" or _ => Encoding.UTF8.GetString(bytes)
        };
    }

    public static Value ImplEnv(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        return Environment.GetEnvironmentVariable(args[0].AsString()) ?? "";
    }

    // --- 采集卡洞函数 ---

    public static Value ImplCaptureHole(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        var result = ctx.Frame?.Invoke(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt());
        return Value.FromString(result ?? "ERR!!FRAME NOT SUPPORT");
    }

    public static Value ImplOcrHole(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        var result = ctx.Ocr?.Invoke(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsString());
        return Value.FromString(result ?? "ERR!!OCR NOT SUPPORT");
    }

    public static Value ImplRoiHole(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        var image = args[0].AsString();
        var result = ctx.Roi?.Invoke(image, args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt());
        return Value.FromString(result ?? "ERR!!ROI NOT SUPPORT");
    }

    // ============ 文件 IO ============

    // ---- 低级句柄 API ----

    public static Value ImplFOpen(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        var path = args[0].AsString();
        var mode = args[1].AsString();
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
                    return Value.FromPtr(-1);
            }
            return Value.FromPtr(handle);
        }
        catch
        {
            return Value.FromPtr(-1);
        }
    }

    public static Value ImplFRead(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        var handle = args[0].AsPtr();
        var count = args[1].AsInt();

        // 标准输入
        if (handle == 0)
        {
            var line = Console.ReadLine() ?? "";
            return Value.FromString(line);
        }

        // 文件句柄
        if (!_readers.TryGetValue(handle, out var reader))
            return Value.FromString("");

        try
        {
            if (count <= 0)
                return Value.FromString(reader.ReadToEnd());

            var buffer = new char[count];
            int read = reader.Read(buffer, 0, count);
            return Value.FromString(new string(buffer, 0, read));
        }
        catch
        {
            return Value.FromString("");
        }
    }

    public static Value ImplFWrite(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        var handle = args[0].AsPtr();
        var data = args[1].AsString();

        // 标准输出 — 走 IoAdapter
        if (handle == 1)
        {
            var s = data;
            var output = s.EndsWith('\\') ? s[..^1] : s;
            ctx.IoAdapter?.Print(output, !ctx.CancelLineBreak);
            ctx.CancelLineBreak = s.EndsWith('\\');
            return Value.FromInt(data.Length);
        }
        // 标准错误
        if (handle == 2)
        {
            ctx.IoAdapter?.Print(data, true);
            return Value.FromInt(data.Length);
        }

        // 文件句柄
        if (!_writers.TryGetValue(handle, out var writer))
            return Value.FromInt(-1);

        try
        {
            writer.Write(data);
            return Value.FromInt(data.Length);
        }
        catch
        {
            return Value.FromInt(-1);
        }
    }

    public static Value ImplFClose(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        var handle = args[0].AsPtr();
        if (handle < 3) return Value.Void;

        if (_readers.TryRemove(handle, out var reader))
            try { reader.Dispose(); } catch { }
        if (_writers.TryRemove(handle, out var writer))
            try { writer.Dispose(); } catch { }

        return Value.Void;
    }

    public static Value ImplFEof(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        var handle = args[0].AsPtr();
        if (handle == 0) return Value.FromBool(false);
        if (!_readers.TryGetValue(handle, out var reader))
            return Value.FromBool(true);
        try { return Value.FromBool(reader.Peek() == -1); }
        catch { return Value.FromBool(true); }
    }

    // ---- 高级便捷 API ----

    public static Value ImplReadFile(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        try { return Value.FromString(File.ReadAllText(args[0].AsString())); }
        catch { return Value.FromString(""); }
    }

    public static Value ImplWriteFile(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        try { File.WriteAllText(args[0].AsString(), args[1].AsString()); } catch { }
        return Value.Void;
    }

    public static Value ImplAppendFile(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        try { File.AppendAllText(args[0].AsString(), args[1].AsString()); } catch { }
        return Value.Void;
    }

    public static Value ImplFileExists(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        return Value.FromBool(File.Exists(args[0].AsString()));
    }

    public static Value ImplOcrConf(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        return Value.FromInt(ctx.OcrConf());
    }

    /// <summary>
    /// 获取所有保留内置函数及其对应的 Callable。
    /// </summary>
    public static ImmutableArray<(FunctionSymbol Symbol, ICallable Callable)> GetAll()
    {
        return
        [
            (BuiltinFunctions.Alert, new DelegateCallable(ImplAlert)),
            (BuiltinFunctions.Amiibo, new DelegateCallable(ImplAmiibo)),
            (BuiltinFunctions.Beep, new DelegateCallable(ImplBeep)),
            (BuiltinFunctions.Env, new DelegateCallable(ImplEnv)),
            (BuiltinFunctions.StrEncode, new DelegateCallable(ImplStrEncode)),
            (BuiltinFunctions.Jq, new DelegateCallable(ImplJq)),
            // 文件 IO
            (BuiltinFunctions.FOpen, new DelegateCallable(ImplFOpen)),
            (BuiltinFunctions.FRead, new DelegateCallable(ImplFRead)),
            (BuiltinFunctions.FWrite, new DelegateCallable(ImplFWrite)),
            (BuiltinFunctions.FClose, new DelegateCallable(ImplFClose)),
            (BuiltinFunctions.FEof, new DelegateCallable(ImplFEof)),
            (BuiltinFunctions.ReadFile, new DelegateCallable(ImplReadFile)),
            (BuiltinFunctions.WriteFile, new DelegateCallable(ImplWriteFile)),
            (BuiltinFunctions.AppendFile, new DelegateCallable(ImplAppendFile)),
            (BuiltinFunctions.FileExists, new DelegateCallable(ImplFileExists)),
            (BuiltinFunctions.OcrConf, new DelegateCallable(ImplOcrConf)),
        ];
    }

    /// <summary>
    /// 获取采集卡洞函数的 Callable 映射。
    /// </summary>
    public static ImmutableArray<(FunctionSymbol Symbol, ICallable Callable)> GetCaptureHoleCallables()
    {
        return
        [
            (BuiltinFunctions.CaptureHole, new DelegateCallable(ImplCaptureHole)),
            (BuiltinFunctions.OcrHole, new DelegateCallable(ImplOcrHole)),
            (BuiltinFunctions.RoiHole, new DelegateCallable(ImplRoiHole)),
        ];
    }
}
