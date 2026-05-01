using EasyCon.Script.Runtime;
using EasyCon.Script.Symbols;
using System.Collections.Immutable;

namespace EasyCon.Script.Binding;

internal sealed class BoundScope(BoundScope? parent)
{
    private readonly Dictionary<string, VariableSymbol> _var_symbols = [];
    private readonly Dictionary<string, List<FunctionSymbol>> _fn_symbols = [];
    private readonly Dictionary<string, EcsStructDef> _structDefs = [];
    private ImmutableHashSet<string> _validExternalVariables = [];

    public BoundScope? Parent { get; } = parent;

    public bool TryDeclareStruct(string name, EcsStructDef def)
    {
        if (_structDefs.ContainsKey(name))
            return false;
        _structDefs[name] = def;
        return true;
    }

    public EcsStructDef? TryLookupStruct(string name)
    {
        if (_structDefs.TryGetValue(name, out var def))
            return def;
        return Parent?.TryLookupStruct(name);
    }

    public ImmutableDictionary<string, EcsStructDef> CollectAllStructDefs()
    {
        var result = new Dictionary<string, EcsStructDef>();
        CollectStructDefs(result);
        return result.ToImmutableDictionary();
    }

    private void CollectStructDefs(Dictionary<string, EcsStructDef> result)
    {
        foreach (var kv in _structDefs)
            result.TryAdd(kv.Key, kv.Value);
        Parent?.CollectStructDefs(result);
    }

    public bool TryDeclareVariable(VariableSymbol variable)
    {
        if (_var_symbols.ContainsKey(variable.Name))
            return false;

        _var_symbols.Add(variable.Name, variable);
        return true;
    }

    public VariableSymbol? TryLookupVar(string name)
    {
        if (_var_symbols.TryGetValue(name, out var symbol))
            return symbol;

        return Parent?.TryLookupVar(name);
    }

    public bool TryDeclareFunction(FunctionSymbol function)
    {
        if (!_fn_symbols.TryGetValue(function.Name, out var list))
        {
            list = [];
            _fn_symbols[function.Name] = list;
        }

        foreach (var existing in list)
        {
            if (function.IsSignatureConflict(existing))
                return false;
        }

        list.Add(function);
        return true;
    }

    public FunctionSymbol? TryLookupFunc(string name)
    {
        if (_fn_symbols.TryGetValue(name, out var list) && list.Count > 0)
            return list[0];

        return Parent?.TryLookupFunc(name);
    }

    public ImmutableArray<FunctionSymbol> TryLookupFuncs(string name)
    {
        if (_fn_symbols.TryGetValue(name, out var list))
            return [.. list];

        return Parent?.TryLookupFuncs(name) ?? [];
    }

    public bool TryFindoutLabel(string name)
    {
        if (_validExternalVariables.Contains(name)) return true;
        return Parent?.TryFindoutLabel(name) ?? false;
    }

    public void SetValidExternalVariables(ImmutableHashSet<string> validNames)
    {
        _validExternalVariables = validNames;
    }

    public ImmutableArray<VariableSymbol> GetDeclaredVariables()
        => [.. _var_symbols.Values];

    public ImmutableArray<FunctionSymbol> GetDeclaredFunctions()
        => [.. _fn_symbols.Values.SelectMany(list => list)];
}