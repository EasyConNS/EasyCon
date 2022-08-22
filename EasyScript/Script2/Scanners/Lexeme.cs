namespace Compiler.Scanners;

public record Lexeme
{
    private Lexer lexer  { get; init; }
    public Token Tag { get; init; }

    public int Pos { get; init; }
    public int Col { get; init; }
    public int Row { get; init; }

    public string Value { get; init; }

    public Lexeme(string value, Token tag, Lexer lex, int pos, int col, int row)
    {
        Value = value;
        Tag = tag;
        lexer = lex;

        Pos = pos;
        Col = col;
        Row = row;
    }
}