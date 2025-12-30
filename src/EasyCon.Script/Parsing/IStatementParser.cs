namespace EasyScript.Parsing;

record ParserArgument
{
    public string Text { get; init; } = string.Empty;
    public Formatter Formatter { get; init; }
    public string Comment { get; init; } = string.Empty;
}
