using System.Text.RegularExpressions;

namespace EasyCon2.Script.Parsing.Statements
{
    class Mov : Statement
    {
        public static readonly IStatementParser Parser = new StatementParser(Parse);
        protected readonly ValRegEx RegDst;
        protected readonly ValBase Value;

        public Mov(ValRegEx regdst, ValBase value)
        {
            RegDst = regdst;
            Value = value;
        }

        public static Statement Parse(ParserArgument args)
        {
            var m = Regex.Match(args.Text, $@"^{Formats.RegisterEx}\s*=\s*{Formats.ValueEx}$", RegexOptions.IgnoreCase);
            if (m.Success)
                return new Mov(Formatter.GetRegEx(m.Groups[1].Value, true), args.Formatter.GetValueEx(m.Groups[2].Value));
            return null;
        }

        protected override string _GetString(Formatter formatter)
        {
            return $"{RegDst.GetCodeText(formatter)} = {Value.GetCodeText(formatter)}";
        }

        public override void Exec(Processor processor)
        {
            processor.Register[RegDst] = Value.Get(processor);
        }

        public override void Assemble(Assembly.Assembler assembler)
        {
            if (RegDst is ValReg32)
                throw new Assembly.AssembleException(ErrorMessage.NotSupported);
            if (RegDst.Index >= Processor.OfflineMaxRegisterCount)
                throw new Assembly.AssembleException(ErrorMessage.RegisterCountNotSupported);
            if (Value is ValRegEx)
            {
                if( (Value as ValRegEx).Index >= Processor.OfflineMaxRegisterCount)
                    throw new Assembly.AssembleException(ErrorMessage.RegisterCountNotSupported);
            }

            assembler.Add(Assembly.Instructions.AsmMov.Create(RegDst.Index, Value));
        }
    }
}
