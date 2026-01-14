using System.Collections.Immutable;

namespace EasyCon.Script.Binding;

internal sealed class BoundScope(BoundScope? parent)
{
    private readonly Dictionary<string, VariableSymbol> _var_symbols = [];
    private readonly Dictionary<string, FunctionSymbol> _fn_symbols = [];

    public BoundScope? Parent { get; } = parent;

    public bool TryDeclareVariable(VariableSymbol variable)
    {
        if (_var_symbols.ContainsKey(variable.Name))
            return false;

        _var_symbols.Add(variable.Name, variable);
        return true;
    }

    public bool TryDeclareFunction(FunctionSymbol function)
    {
        if (_fn_symbols.ContainsKey(function.Name))
            return false;

        _fn_symbols.Add(function.Name, function);
        return true;
    }

    public VariableSymbol? TryLookupVar(string name)
    {
        if (_var_symbols.TryGetValue(name, out var symbol))
            return symbol;

        return Parent?.TryLookupVar(name);
    }
    public FunctionSymbol? TryLookupFunc(string name)
    {
        if (_fn_symbols.TryGetValue(name, out var symbol))
            return symbol;

        return Parent?.TryLookupFunc(name);
    }

    public ImmutableArray<VariableSymbol> GetDeclaredVariables()
        => [.. _var_symbols.Values];

    public ImmutableArray<FunctionSymbol> GetDeclaredFunctions()
        => [.. _fn_symbols.Values];
}