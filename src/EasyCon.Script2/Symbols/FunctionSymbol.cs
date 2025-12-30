using System.Collections.Immutable;
using EasyCon.Script2.Ast;

namespace EasyCon.Script2.Symbols;

public sealed class FunctionSymbol : Symbol
{
    internal FunctionSymbol(string name, ImmutableArray<ParameterSymbol> parameters, TypeSymbol type, FunctionDeclarationStatement? declaration = null)
        : base(name)
    {
        Parameters = parameters;
        Type = type;
        Declaration = declaration;
    }

    public override SymbolKind Kind => SymbolKind.Function;
    public FunctionDeclarationStatement? Declaration { get; }

    public ImmutableArray<ParameterSymbol> Parameters { get; }
    public TypeSymbol Type { get; }
}