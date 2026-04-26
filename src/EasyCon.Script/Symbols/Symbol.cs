using EasyCon.Script.Syntax;
using System.Collections.Immutable;

namespace EasyCon.Script.Symbols;

abstract class Symbol(string name)
{
    public readonly string Name = name;
}

abstract class VariableSymbol(string name, bool isReadOnly, ScriptType type) : Symbol(name)
{
    public readonly ScriptType Type = type;
    public readonly bool IsReadOnly = isReadOnly;
    internal object? Value = null;
}

sealed class GlobalVariableSymbol(string name, bool isReadOnly, ScriptType type) : VariableSymbol(name, isReadOnly, type)
{
    public override string ToString() => $"GlobalVar({Name}: {Type})";
}

class LocalVariableSymbol(string name, bool isReadOnly, ScriptType type) : VariableSymbol(name, isReadOnly, type)
{
    public override string ToString() => $"LocalVar({Name}: {Type})";
}

sealed class ParamSymbol(string name, ScriptType type, int ordinal = 0, bool hasDefault = false, object? defaultValue = null) : LocalVariableSymbol(name, true, type)
{
    public int Ordinal { get; } = ordinal;
    public bool HasDefaultValue { get; } = hasDefault;
    public object? DefaultValue { get; } = defaultValue;
}

/// <summary>
/// 支持泛型的函数符号
/// </summary>
sealed class FunctionSymbol(
    string name,
    IEnumerable<TypeParameter> typeParameters,
    IEnumerable<ParamSymbol> parameters,
    ScriptType returnType,
    FuncDeclBlock? declaration = null,
    string libraryName = "internal") : Symbol(name)
{
    public ImmutableArray<TypeParameter> TypeParameters { get; } = [.. typeParameters];
    public ImmutableArray<ParamSymbol> Parameters { get; } = [.. parameters];
    public readonly FuncDeclBlock? Declaration = declaration;
    public ScriptType ReturnType { get; } = returnType;
    public readonly string LibraryName = libraryName;

    public override string ToString() => $"Func({Name}: {ReturnType})";
    /// <summary>
    /// 辅助构造：非泛型函数
    /// </summary>
    public static FunctionSymbol CreateNormal(string name, IEnumerable<ParamSymbol> parameters, ScriptType returnType)
        => new(name, [], parameters, returnType);
}