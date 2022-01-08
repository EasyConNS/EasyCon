using System.Text.RegularExpressions;

namespace EasyCon2.Script.Parsing.Statements
{
    class Wait : Statement
    {
        public static readonly IStatementParser Parser = new StatementParser(Parse);
        protected readonly ValBase Duration;
        protected bool _omitted;

        public Wait(ValBase duration, bool omitted = false)
        {
            Duration = duration;
            _omitted = omitted;
        }

        public static Statement Parse(ParserArgument args)
        {
            int duration;
            if (int.TryParse(args.Text, out duration))
                return new Wait(duration, true);
            var m = Regex.Match(args.Text, $@"^wait\s+{Formats.ValueEx}$", RegexOptions.IgnoreCase);
            if (m.Success)
                return new Wait(args.Formatter.GetValueEx(m.Groups[1].Value));
            return null;
        }

        protected override string _GetString(Formatter formatter)
        {
            return _omitted ? $"{Duration.GetCodeText(formatter)}" : $"WAIT {Duration.GetCodeText(formatter)}";
        }

        public override void Exec(Processor processor)
        {
            Thread.Sleep(Duration.Get(processor));
        }

        public override void Assemble(Assembly.Assembler assembler)
        {
            if (Duration is ValReg)
            {
                assembler.Add(Assembly.Instructions.AsmStoreOp.Create((Duration as ValReg).Index));
                assembler.Add(Assembly.Instructions.AsmWait.Create(0));
            }
            else if (Duration is ValInstant)
                assembler.Add(Assembly.Instructions.AsmWait.Create((Duration as ValInstant).Val));
            else
                throw new Assembly.AssembleException(ErrorMessage.NotSupported);
        }
    }
}
