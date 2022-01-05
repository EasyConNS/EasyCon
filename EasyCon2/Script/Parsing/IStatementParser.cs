namespace EasyCon2.Script.Parsing
{
    interface IStatementParser
    {
        Statement Parse(ParserArgument args);
    }

    class StatementParser : IStatementParser
    {
        public delegate Statement ParserDelegate(ParserArgument args);

        readonly ParserDelegate _parse;

        public StatementParser(ParserDelegate parse)
        {
            _parse = parse;
        }

        public Statement Parse(ParserArgument args)
        {
            return _parse(args);
        }
    }

    class ParserArgument
    {
        public string Text;
        public Formats.Formatter Formatter;
    }
}
