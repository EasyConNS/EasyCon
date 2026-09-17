using EasyCon.Core.Capabilities;
using EasyCon.Script.Symbols;
using EasyScript;

namespace EasyCon.Core.Runner;

/// <summary>
/// 统一可调用接口。内置函数、FFI 函数均通过此接口执行；宿主能力以
/// <see cref="CapabilitySet"/> 注入（v1 IEvalContext 已删除）。
/// </summary>
internal interface ICallable
{
    Value Invoke(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token);
}

/// <summary>
/// 基于委托的通用 ICallable 实现。
/// </summary>
internal delegate Value CallableDelegate(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token);

internal sealed class DelegateCallable(CallableDelegate impl) : ICallable
{
    public Value Invoke(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
        => impl(args, capabilities, token);
}