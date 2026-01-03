namespace EasyCon.Script.Binding;

abstract class Symbol(string name)
{
    public readonly string Name = name;
}

abstract class VariableSymbol(string name) : Symbol(name)
{
    internal object? Value { get; }
}

sealed class GlobalVariableSymbol(string name) : VariableSymbol(name)
{
    //
}

class LocalVariableSymbol(string name) : VariableSymbol(name)
{
    //
}

sealed class FunctionSymbol(string name, int paramters = 0) : Symbol(name)
{
    public readonly int Paramters = paramters;
}