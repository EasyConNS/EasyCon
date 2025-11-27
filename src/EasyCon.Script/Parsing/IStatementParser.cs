namespace EasyScript.Parsing;

interface IStatementParser
{
    Statement? Parse(ParserArgument args);
}

record ParserArgument
{
    public string Text { get; init; } = string.Empty;
    public Formatter Formatter { get; init; }
    public string Comment { get; init; } = string.Empty;
}
