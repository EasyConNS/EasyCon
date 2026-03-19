using EasyCon.Script.Parsing;
using System.Collections.Immutable;

namespace EasyCon.Script.Binding;

abstract class Symbol(string name)
{
    public readonly string Name = name;
}

public enum ValueType
{
    Void,
    Int,
    Bool,
    String,
    Array,
}

abstract class VariableSymbol(string name, bool isReadOnly, ValueType valueType) : Symbol(name)
{
    public readonly ValueType Type = valueType;
    public readonly bool IsReadOnly = isReadOnly;
    internal object? Value;
}

sealed class GlobalVariableSymbol(string name, bool isReadOnly, ValueType valueType) : VariableSymbol(name, isReadOnly, valueType)
{
}

class LocalVariableSymbol(string name, bool isReadOnly, ValueType valueType) : VariableSymbol(name, isReadOnly, valueType)
{
}

sealed class ParamSymbol(string name, ValueType valueType, int ordinal = 0) : LocalVariableSymbol(name, true, valueType)
{
    public int Ordinal { get; } = ordinal;
}

sealed class FunctionSymbol(string name, ImmutableArray<ParamSymbol> paramters, ValueType type, FuncDeclBlock? declaration = null) : Symbol(name)
{
    public readonly ImmutableArray<ParamSymbol> Paramters = paramters;
    public readonly FuncDeclBlock? Declaration = declaration;
    public readonly ValueType Type = type;
}