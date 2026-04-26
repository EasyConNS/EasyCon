using EasyCon.Script.Symbols;
using EasyScript;
using System.Collections.Immutable;

namespace EasyCon.Script.Binding;

/// <summary>
/// 统一可调用接口。内置函数、用户函数、FFI 函数均通过此接口执行。
/// </summary>
internal interface ICallable
{
    Value Invoke(ImmutableArray<Value> args, IEvalContext context, CancellationToken token);
}

/// <summary>
/// 评估上下文接口，抽象 Evaluator 内部状态访问。
/// </summary>
internal interface IEvalContext
{
    ICGamePad? GamePad { get; }
    IOutputAdapter? Output { get; }
    Random Rand { get; }
    bool CancelLineBreak { get; set; }

    void PushLocals(Dictionary<VariableSymbol, Value> locals);
    void PopLocals();
    Value EvaluateFunctionBody(FunctionSymbol function);
}

/// <summary>
/// 基于委托的通用 ICallable 实现。
/// </summary>
internal sealed class DelegateCallable(Func<ImmutableArray<Value>, IEvalContext, CancellationToken, Value> impl) : ICallable
{
    public Value Invoke(ImmutableArray<Value> args, IEvalContext context, CancellationToken token)
        => impl(args, context, token);
}