namespace ECScript.Syntax;

public sealed class SyntaxTrivia
{
    internal SyntaxTrivia(TokenType kind, string text)
    {
        Kind = kind;
        Text = text;
    }
    public TokenType Kind { get; init; }
    public string Text { get; init; }
}