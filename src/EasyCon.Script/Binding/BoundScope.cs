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
            if (existing.Parameters.Length != function.Parameters.Length) continue;
            bool conflict = true;
            for (int i = 0; i < existing.Parameters.Length; i++)
            {
                if (!existing.Parameters[i].Type.Equals(function.Parameters[i].Type))
                {
                    conflict = false;
                    break;
                }
            }
            if (conflict) return false;
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

    /// <summary>
    /// 从另一个作用域导入所有符号（函数、变量、结构体）到当前作用域。
    /// </summary>
    public void ImportFrom(BoundScope source)
    {
        foreach (var fn in source.GetDeclaredFunctions())
            TryDeclareFunction(fn);
        foreach (var v in source.GetDeclaredVariables())
            TryDeclareVariable(v);
        foreach (var kv in source.CollectAllStructDefs())
            TryDeclareStruct(kv.Key, kv.Value);
    }
}