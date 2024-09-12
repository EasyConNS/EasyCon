using System.Collections.Immutable;

namespace EC.Script.Syntax;

public sealed partial class MainUnitSyntax : SyntaxNode
{
    internal MainUnitSyntax(SyntaxTree syntaxTree, ImmutableArray<MemberSyntax> members) : base(syntaxTree) { }

    public override TokenType Kind { get => throw new NotImplementedException(); init => throw new NotImplementedException(); }

    public ImmutableArray<MemberSyntax> Members { get; init; }

    public override IEnumerable<SyntaxNode> GetChildren()
    {
        throw new NotImplementedException();
    }
}
