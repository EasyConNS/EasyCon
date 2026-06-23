using EasyCon.Script.Symbols;
using EasyScript;

namespace EasyCon.Core.Runner;

/// <summary>
/// 统一可调用接口。内置函数、用户函数、FFI 函数均通过此接口执行。
/// </summary>
internal interface ICallable
{
    Value Invoke(ReadOnlySpan<Value> args, IEvalContext context, CancellationToken token);
}

/// <summary>
/// 评估上下文接口，抽象 Evaluator 内部状态访问。
/// </summary>
internal interface IEvalContext
{
    ICGamePad? GamePad { get; }
    IIoAdapter? IoAdapter { get; }
    OcrDelegate? Ocr { get; }
    OcrInitDelegate? OcrInit { get; }
    Func<int> OcrConf { get; }
    FrameDelegate? Frame { get; }
    RoiDelegate? Roi { get; }
    LabelMatchDelegate? LabelMatch { get; }
    Random Rand { get; }
    int Timestamp { get; }
    bool CancelLineBreak { get; set; }
    string[] Args { get; }

    Value EvaluateFunctionBody(FunctionSymbol function);
}

/// <summary>
/// 基于委托的通用 ICallable 实现。
/// </summary>
internal delegate Value CallableDelegate(ReadOnlySpan<Value> args, IEvalContext context, CancellationToken token);

internal sealed class DelegateCallable(CallableDelegate impl) : ICallable
{
    public Value Invoke(ReadOnlySpan<Value> args, IEvalContext context, CancellationToken token)
        => impl(args, context, token);
}