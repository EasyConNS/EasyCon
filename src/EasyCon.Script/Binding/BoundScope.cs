using System.Collections.Immutable;

namespace EasyCon.Script.Binding;

internal sealed class BoundScope(BoundScope? parent)
{
    private Dictionary<string, Symbol>? _symbols;

    public BoundScope? Parent { get; } = parent;

    public bool TryDeclareVariable(VariableSymbol variable)
        => TryDeclareSymbol(variable);

    public bool TryDeclareFunction(FunctionSymbol function)
        => TryDeclareSymbol(function);

    private bool TryDeclareSymbol<TSymbol>(TSymbol symbol)
        where TSymbol : Symbol
    {
        if (_symbols == null)
            _symbols = [];
        else if (_symbols.ContainsKey(symbol.Name))
            return false;

        _symbols.Add(symbol.Name, symbol);
        return true;
    }

    public Symbol? TryLookupSymbol(string name)
    {
        if (_symbols != null && _symbols.TryGetValue(name, out var symbol))
            return symbol;

        return Parent?.TryLookupSymbol(name);
    }

    public ImmutableArray<VariableSymbol> GetDeclaredVariables()
        => GetDeclaredSymbols<VariableSymbol>();

    public ImmutableArray<FunctionSymbol> GetDeclaredFunctions()
        => GetDeclaredSymbols<FunctionSymbol>();

    private ImmutableArray<TSymbol> GetDeclaredSymbols<TSymbol>()
        where TSymbol : Symbol
    {
        if (_symbols == null)
            return [];

        return [.. _symbols.Values.OfType<TSymbol>()];
    }
}