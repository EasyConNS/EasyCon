using System;
using EasyCon.Script.Ssa;
using EasyCon.Script.Symbols;

namespace EasyCon.Core.Runner;

/// <summary>
/// JIT 桥接层：JIT 编译的 C# 代码通过此桥接回调 SsaEvaluator 执行函数调用。
/// 所有方法接受 SsaEvaluator 作为第一个参数，避免静态状态跨调用污染。
/// </summary>
public static class SsaJitInterop
{
    public static int CallInt0(SsaEvaluator eval, SsaValue[] ops, int opId)
    {
        var val = ops[opId];
        var func = (FunctionSymbol)val.Aux!;
        var result = InvokeCall(eval, func, ReadOnlySpan<Value>.Empty);
        return UnpackInt(eval, opId, func.ReturnType, result);
    }

    public static int CallInt1(SsaEvaluator eval, SsaValue[] ops, int opId, int arg0)
    {
        var val = ops[opId];
        var func = (FunctionSymbol)val.Aux!;
        var result = InvokeCall(eval, func, [Value.FromInt(arg0)]);
        return UnpackInt(eval, opId, func.ReturnType, result);
    }

    public static int CallInt2(SsaEvaluator eval, SsaValue[] ops, int opId, int arg0, int arg1)
    {
        var val = ops[opId];
        var func = (FunctionSymbol)val.Aux!;
        var result = InvokeCall(eval, func, [Value.FromInt(arg0), Value.FromInt(arg1)]);
        return UnpackInt(eval, opId, func.ReturnType, result);
    }

    public static int CallInt3(SsaEvaluator eval, SsaValue[] ops, int opId, int arg0, int arg1, int arg2)
    {
        var val = ops[opId];
        var func = (FunctionSymbol)val.Aux!;
        var result = InvokeCall(eval, func, [Value.FromInt(arg0), Value.FromInt(arg1), Value.FromInt(arg2)]);
        return UnpackInt(eval, opId, func.ReturnType, result);
    }

    public static int CallInt4(SsaEvaluator eval, SsaValue[] ops, int opId, int arg0, int arg1, int arg2, int arg3)
    {
        var val = ops[opId];
        var func = (FunctionSymbol)val.Aux!;
        var result = InvokeCall(eval, func, [Value.FromInt(arg0), Value.FromInt(arg1), Value.FromInt(arg2), Value.FromInt(arg3)]);
        return UnpackInt(eval, opId, func.ReturnType, result);
    }

    private static Value InvokeCall(SsaEvaluator eval, FunctionSymbol func, ReadOnlySpan<Value> args)
    {
        if (eval.IsUserFunction(func))
            return eval.CallFunction(func, args);
        return eval.CallExternal(func, args);
    }

    private static int UnpackInt(SsaEvaluator eval, int resultId, ScriptType returnType, Value result)
    {
        if (returnType.Equals(ScriptType.Void)) return 0;
        var val = result.AsInt();
        eval.Cache[resultId] = TaggedValue.FromInt(val);
        return val;
    }
}