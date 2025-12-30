using EasyCon.Script2.Binding;

namespace EasyCon.Script2.Symbols;

public class LocalVariableSymbol : VariableSymbol
{
    internal LocalVariableSymbol(string name, bool isReadOnly, TypeSymbol type, BoundConstant? constant)
        : base(name, isReadOnly, type, constant)
    {
    }

    public override SymbolKind Kind => SymbolKind.LocalVariable;
}