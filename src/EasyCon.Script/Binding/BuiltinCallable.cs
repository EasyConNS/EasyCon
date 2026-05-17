using EasyCon.Script.Symbols;
using EasyScript;
using System.Collections.Immutable;
using System.Text;

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

    public static Value ImplTimestamp(ReadOnlySpan<Value> _, IEvalContext ctx, CancellationToken token)
    {
        return ctx.Timestamp;
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

    public static Value ImplOcr(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        var result = ctx.Image?.OCR(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[3].AsString()) ?? "OCR NOT SUPPORT";
        return Value.FromString(result);
    }

    public static Value ImplJq(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        // JQ implementation placeholder
        throw new NotImplementedException("function not implemented");
    }

    public static Value ImplConvertInt(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        return args[0].ToInt();
    }

    public static Value ImplConvertString(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        return args[0].ToString();
    }

    public static Value ImplLength(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        return args[0].Length;
    }

    public static Value ImplAppend(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        return args[0].Append(args[1]);
    }

    public static Value ImplStrEncode(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        var array = args[0].AsArray();
        var bytes = new byte[array.Count];
        for (int i = 0; i < array.Count; i++)
            bytes[i] = array[i].AsByte();
        return args[1].AsString() switch
        {
            "unicode" => Encoding.Unicode.GetString(bytes),
            "utf8" or _ => Encoding.UTF8.GetString(bytes)
        };
    }

    /// <summary>
    /// 获取所有内置函数及其对应的 Callable。
    /// Timestamp 需要额外的 timestampFactory 闭包参数。
    /// </summary>
    public static ImmutableArray<(FunctionSymbol Symbol, ICallable Callable)> GetAll()
    {
        return
        [
            (BuiltinFunctions.Wait, new DelegateCallable(ImplWait)),
            (BuiltinFunctions.Print, new DelegateCallable(ImplPrint)),
            (BuiltinFunctions.Alert, new DelegateCallable(ImplAlert)),
            (BuiltinFunctions.Rand, new DelegateCallable(ImplRand)),
            (BuiltinFunctions.Timestamp, new DelegateCallable(ImplTimestamp)),
            (BuiltinFunctions.Amiibo, new DelegateCallable(ImplAmiibo)),
            (BuiltinFunctions.Beep, new DelegateCallable(ImplBeep)),
            (BuiltinFunctions.Ocr, new DelegateCallable(ImplOcr)),
            (BuiltinFunctions.Length, new DelegateCallable(ImplLength)),
            (BuiltinFunctions.Append, new DelegateCallable(ImplAppend)),
            (BuiltinFunctions.StrEncode, new DelegateCallable(ImplStrEncode)),
            (BuiltinFunctions.IntConvert, new DelegateCallable(ImplConvertInt)),
            (BuiltinFunctions.StrConvert, new DelegateCallable(ImplConvertString)),
        ];
    }
}