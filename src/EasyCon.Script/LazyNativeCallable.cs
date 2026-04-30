using EasyCon.Script.Binding;
using EasyCon.Script.Symbols;
using EasyScript;

namespace EasyCon.Script;

/// <summary>
/// 懒加载的外部函数 callable。首次调用时才加载库并解析函数地址，后续调用直接委托。
/// </summary>
internal sealed class LazyNativeCallable : ICallable
{
    private readonly FunctionSymbol _symbol;
    private readonly NativeLoader _loader;
    private readonly Lazy<ICallable> _resolved;

    public LazyNativeCallable(FunctionSymbol symbol, NativeLoader loader)
    {
        _symbol = symbol;
        _loader = loader;
        _resolved = new Lazy<ICallable>(() => _loader.ResolveFunction(_symbol));
    }

    public Value Invoke(ReadOnlySpan<Value> args, IEvalContext context, CancellationToken token)
        => _resolved.Value.Invoke(args, context, token);
}
