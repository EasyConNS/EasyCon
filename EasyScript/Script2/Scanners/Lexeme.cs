namespace Compiler.Scanners;

public record Lexeme
{
    private Lexer lexer  { get; init; }
    public Token Tag { get; init; }

    public SourceSpan Span { get; init; }

    public string Value { get; init; }

    public Lexeme(string value, Token tag, Lexer lex, int pos, int col, int row)
    {
        Value = value;
        Tag = tag;
        lexer = lex;
        Span = new(pos, col, row);
    }
}