using System.Text.RegularExpressions;

namespace EasyCon2.Script.Parsing.Statements
{
    class TimeStamp : Statement
    {
        public static readonly IStatementParser Parser = new StatementParser(Parse);
        private readonly ValRegEx RegDst;

        public static Statement Parse(ParserArgument args)
        {
            var m = Regex.Match(args.Text, $@"^TIME\s+{Formats.RegisterEx}$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                return new TimeStamp(Formatter.GetRegEx(m.Groups[1].Value));
            }
            return null;
        }

        public TimeStamp(ValRegEx val)
        {
            RegDst = val;
        }

        public override void Exec(Processor processor)
        {
            processor.Register[RegDst] = processor.et.CurrTimestamp;
        }

        public override void Assemble(Assembly.Assembler assembler)
        {
            throw new Assembly.AssembleException(ErrorMessage.NotSupported);
        }

        protected override string _GetString(Formatter formatter)
        {
            return $"TIME {RegDst.GetCodeText(formatter)}";
        }
    }
}
