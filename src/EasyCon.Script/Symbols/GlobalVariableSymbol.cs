using EasyCon.Script2.Binding;

namespace EasyCon.Script2.Symbols;

public sealed class GlobalVariableSymbol : VariableSymbol
{
    internal GlobalVariableSymbol(string name, bool isReadOnly, TypeSymbol type, BoundConstant? constant)
        : base(name, isReadOnly, type, constant)
    {
    }

    public override SymbolKind Kind => SymbolKind.GlobalVariable;
}