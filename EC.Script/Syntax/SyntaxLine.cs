using System.Collections.Immutable;

namespace EC.Script.Syntax;

public sealed partial class SyntaxLine
{
    internal SyntaxLine(ImmutableArray<SyntaxToken> tokens, SyntaxTrivia trailingTrivia, uint index)
    {
        Tokens = tokens;
        TrailingTrivia = trailingTrivia;
        Line = index;
    }

    public ImmutableArray<SyntaxToken> Tokens { get; init; }

    public SyntaxTrivia TrailingTrivia { get; init; }

    public uint Line { get; init; }
}
