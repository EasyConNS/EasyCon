using EasyCon.Script.Runtime;
using EasyCon.Script.Symbols;
using EasyScript;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace EasyCon.Script.Binding;

/// <summary>
/// 内置函数 Callable 实现。每个静态方法对应一个内置函数的执行逻辑。
/// </summary>
internal static class BuiltinCallable
{
    public static Value ImplWait(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        var ms = args[0].AsInt();
        CustomDelay.Delay(ms, token);
        return Value.Void;
    }

    public static Value ImplPrint(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        var s = args[0].AsString();
        var output = s.EndsWith('\\') ? s[..^1] : s;
        ctx.Output?.Print(output, !ctx.CancelLineBreak);
        ctx.CancelLineBreak = s.EndsWith('\\');
        return Value.Void;
    }

    public static Value ImplAlert(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        var s = args[0].AsString();
        var output = s.EndsWith('\\') ? s[..^1] : s;
        ctx.Output?.Alert(output);
        return Value.Void;
    }

    public static Value ImplRand(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        var max = args[0].AsInt();
        max = max < 0 ? 0 : max;
        return ctx.Rand.Next(max);
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

            return current.ValueKind switch
            {
                JsonValueKind.Number => current.GetInt32(),
                JsonValueKind.String => Value.FromString(current.GetString()!),
                JsonValueKind.True => Value.FromBool(true),
                JsonValueKind.False => Value.FromBool(false),
                JsonValueKind.Array => Value.CreateArray(ScriptType.Int,
                    current.EnumerateArray().Select(e => (Value)e.GetInt32())),
                _ => Value.FromString(current.GetRawText()),
            };
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

    /// <summary>
    /// 获取所有保留内置函数及其对应的 Callable。
    /// </summary>
    public static ImmutableArray<(FunctionSymbol Symbol, ICallable Callable)> GetAll()
    {
        return
        [
            (BuiltinFunctions.Wait, new DelegateCallable(ImplWait)),
            (BuiltinFunctions.Print, new DelegateCallable(ImplPrint)),
            (BuiltinFunctions.Alert, new DelegateCallable(ImplAlert)),
            (BuiltinFunctions.Rand, new DelegateCallable(ImplRand)),
            (BuiltinFunctions.Amiibo, new DelegateCallable(ImplAmiibo)),
            (BuiltinFunctions.Beep, new DelegateCallable(ImplBeep)),
            (BuiltinFunctions.Env, new DelegateCallable(ImplEnv)),
            (BuiltinFunctions.StrEncode, new DelegateCallable(ImplStrEncode)),
            (BuiltinFunctions.Jq, new DelegateCallable(ImplJq)),
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