namespace EasyCon2.Script.Parsing
{
    interface IStatementParser
    {
        Statement? Parse(ParserArgument args);
    }

    record ParserArgument
    {
        public string Text { get; init; }
        public Formatter Formatter { get; init; }
    }
}
