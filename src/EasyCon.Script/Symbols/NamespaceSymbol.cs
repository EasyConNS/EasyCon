using EasyCon.Script.Runtime;
using System.Collections.Immutable;

namespace EasyCon.Script.Symbols;

/// <summary>
/// 表示一个命名空间，作为符号的容器。
/// </summary>
public sealed class NamespaceSymbol : Symbol
{
    /// <summary>
    /// 命名空间包含的函数
    /// </summary>
    private readonly Dictionary<string, List<FunctionSymbol>> _functions = [];

    /// <summary>
    /// 命名空间包含的结构体定义
    /// </summary>
    private readonly Dictionary<string, EcsStructDef> _structDefs = [];

    public NamespaceSymbol(string name) : base(name) { }

    public bool TryDeclareFunction(FunctionSymbol function)
    {
        if (!_functions.TryGetValue(function.Name, out var list))
        {
            list = [];
            _functions[function.Name] = list;
        }
        list.Add(function);
        return true;
    }

    public ImmutableArray<FunctionSymbol> GetFunctions(string name)
    {
        if (_functions.TryGetValue(name, out var list))
            return [.. list];
        return [];
    }

    public bool TryDeclareStruct(string name, EcsStructDef def)
    {
        return _structDefs.TryAdd(name, def);
    }

    public EcsStructDef? GetStruct(string name)
    {
        return _structDefs.GetValueOrDefault(name);
    }
}