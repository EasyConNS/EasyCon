using EC.Script.Text;

namespace EC.Script.Syntax;

public sealed class SyntaxToken : SyntaxNode
{
    internal SyntaxToken(TokenType kind, string? text, SourceSpan span)
    {
        Kind = kind;
        Text = text ?? string.Empty;
        Span = span;
    }

    public override SourceSpan Span { get; }

    public override TokenType Kind { get; init; }

    public string Text { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => [];
}