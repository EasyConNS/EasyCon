using EasyCon.Script.Symbols;
using EasyScript;
using System.Collections.Immutable;

namespace EasyCon.Script.Binding;

/// <summary>
/// 内置函数 Callable 实现。每个静态方法对应一个内置函数的执行逻辑。
/// </summary>
internal static class BuiltinCallable
{
    public static Value ImplWait(ImmutableArray<Value> args, IEvalContext ctx, CancellationToken token)
    {
        var ms = args[0].AsInt();
        switch (ctx.GamePad?.DelayMethod)
        {
            case DelayType.Normal: Thread.Sleep(ms); break;
            case DelayType.LowCPU: CustomDelay.AISleep(ms); break;
            case DelayType.HighResolution:
            default:
                CustomDelay.Delay(ms, token);
                break;
        }
        return Value.Void;
    }

    public static Value ImplPrint(ImmutableArray<Value> args, IEvalContext ctx, CancellationToken token)
    {
        var s = args[0].AsString();
        var output = s.EndsWith('\\') ? s[..^1] : s;
        ctx.Output?.Print(output, !ctx.CancelLineBreak);
        ctx.CancelLineBreak = s.EndsWith('\\');
        return Value.Void;
    }

    public static Value ImplAlert(ImmutableArray<Value> args, IEvalContext ctx, CancellationToken token)
    {
        var s = args[0].AsString();
        var output = s.EndsWith('\\') ? s[..^1] : s;
        ctx.Output?.Alert(output);
        return Value.Void;
    }

    public static Value ImplRand(ImmutableArray<Value> args, IEvalContext ctx, CancellationToken token)
    {
        var max = args[0].AsInt();
        max = max < 0 ? 0 : max;
        return ctx.Rand.Next(max);
    }

    public static Value ImplTimestamp(ImmutableArray<Value> args, IEvalContext ctx, CancellationToken token)
    {
        // Timestamp 通过 Evaluator 的 _TIME 字段计算，需要特殊处理
        // 由 Evaluator 在注册时通过闭包捕获
        throw new InvalidOperationException("Timestamp 应通过闭包注册");
    }

    public static Value ImplAmiibo(ImmutableArray<Value> args, IEvalContext ctx, CancellationToken token)
    {
        var index = args[0].AsInt();
        if (index > 9) return Value.Void;
        ctx.GamePad?.ChangeAmiibo((uint)index);
        return Value.Void;
    }

    public static Value ImplBeep(ImmutableArray<Value> args, IEvalContext ctx, CancellationToken token)
    {
        var freq = args[0].AsInt();
        if (freq < 37 || freq > 32767) throw new Exception("BEEP参数freq范围不正确(37~32767)");
        Console.Beep(freq, args[1].AsInt());
        return Value.Void;
    }

    public static Value ImplLength(ImmutableArray<Value> args, IEvalContext ctx, CancellationToken token)
    {
        return args[0].Length;
    }

    public static Value ImplAppend(ImmutableArray<Value> args, IEvalContext ctx, CancellationToken token)
    {
        return args[0].Append(args[1]);
    }

    /// <summary>
    /// 获取所有内置函数及其对应的 Callable。
    /// Timestamp 需要额外的 timestampFactory 闭包参数。
    /// </summary>
    public static ImmutableArray<(FunctionSymbol Symbol, ICallable Callable)> GetAll(Func<int> timestampFactory)
    {
        return
        [
            (BuiltinFunctions.Wait, new DelegateCallable(ImplWait)),
            (BuiltinFunctions.Print, new DelegateCallable(ImplPrint)),
            (BuiltinFunctions.Alert, new DelegateCallable(ImplAlert)),
            (BuiltinFunctions.Rand, new DelegateCallable(ImplRand)),
            (BuiltinFunctions.Timestamp, new DelegateCallable((args, ctx, token) => timestampFactory())),
            (BuiltinFunctions.Amiibo, new DelegateCallable(ImplAmiibo)),
            (BuiltinFunctions.Beep, new DelegateCallable(ImplBeep)),
            (BuiltinFunctions.Length, new DelegateCallable(ImplLength)),
            (BuiltinFunctions.Append, new DelegateCallable(ImplAppend)),
        ];
    }
}