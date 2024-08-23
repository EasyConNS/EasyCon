using System.Collections.Immutable;

namespace ECScript.Syntax;

public sealed class SyntaxToken : SyntaxNode
{
    internal SyntaxToken(TokenType kind, string text, ImmutableArray<SyntaxTrivia> trailingTrivia)
    {
        Kind = kind;
        TrailingTrivia = trailingTrivia;
    }

    public override TokenType Kind { get; init; }

    public ImmutableArray<SyntaxTrivia> TrailingTrivia { get; init; }

    public override IEnumerable<SyntaxNode> GetChildren() => [];
}