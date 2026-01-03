namespace EasyCon.Script2.Symbols;

public sealed class TypeSymbol  : Symbol
{
    public static readonly TypeSymbol Error = new("?");
    public static readonly TypeSymbol Any = new("any");
    public static readonly TypeSymbol Bool = new("bool");
    public static readonly TypeSymbol Int = new("int");
    public static readonly TypeSymbol String = new("string");
    public static readonly TypeSymbol Void = new("void");

    private TypeSymbol(string name)
        : base(name)
    {
    }

    public override SymbolKind Kind => SymbolKind.Type;
}