namespace EC.Script.Syntax;

public class Token
{
    public TokenType Type { get; set; }
    public string Value { get; set; }
    public int LineNumber { get; set; }

    public Token(TokenType type, string value, int lineNumber)
    {
        Type = type;
        Value = value;
        LineNumber = lineNumber;
    }
}