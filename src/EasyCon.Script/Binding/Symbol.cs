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
}

abstract class VariableSymbol(string name, bool isReadOnly, ValueType valueType) : Symbol(name)
{
    public readonly ValueType Type = valueType;
    public readonly bool IsReadOnly = isReadOnly;
    internal object? Value { get; }
}

sealed class GlobalVariableSymbol(string name, bool isReadOnly, ValueType valueType) : VariableSymbol(name, isReadOnly, valueType)
{
}

class LocalVariableSymbol(string name, bool isReadOnly, ValueType valueType) : VariableSymbol(name, isReadOnly, valueType)
{
}

sealed class FunctionSymbol(string name, ImmutableArray<ValueType> paramters, FuncDeclBlock? declaration = null) : Symbol(name)
{
    public readonly ImmutableArray<ValueType> Paramters = paramters;
    public readonly FuncDeclBlock? Declaration = declaration;
}