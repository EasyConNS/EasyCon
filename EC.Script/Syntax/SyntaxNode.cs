using ECScript.Text;

namespace ECScript.Syntax;

public abstract class SyntaxNode
{
    public abstract TokenType Kind { get; init; }

    public virtual SourceSpan Span
    {
        get
        {
            var first = GetChildren().First().Span;
            var last = GetChildren().Last().Span;
            return SourceSpan.FromBounds(first.Start, last.End, first.Line);
        }
    }

    public abstract IEnumerable<SyntaxNode> GetChildren();

    public SyntaxToken GetLastToken()
    {
        if (this is SyntaxToken token)
            return token;

        // A syntax node should always contain at least 1 token.
        return GetChildren().Last().GetLastToken();
    }
}
