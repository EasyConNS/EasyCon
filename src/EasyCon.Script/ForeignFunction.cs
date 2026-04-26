using EasyCon.Script.Symbols;
using System.Collections.Immutable;

namespace EasyCon.Script;

/// <summary>
/// FFI（外部函数接口）注册描述符。
/// 用于向脚本引擎注册宿主端提供的函数。
/// </summary>
internal readonly struct ForeignFunction
{
    /// <summary>函数名称（脚本中通过此名称调用）</summary>
    public required string Name { get; init; }

    /// <summary>参数列表，每项包含名称和类型</summary>
    public required (string Name, ScriptType Type)[] Parameters { get; init; }

    /// <summary>返回值类型</summary>
    public ScriptType ReturnType { get; init; }

    /// <summary>函数执行委托，接收参数值数组，返回脚本值</summary>
    public required Func<ImmutableArray<Value>, Value> Invoke { get; init; }

    public ForeignFunction()
    {
        Name = "";
        Parameters = [];
        ReturnType = ScriptType.Void;
        Invoke = static _ => Value.Void;
    }
}