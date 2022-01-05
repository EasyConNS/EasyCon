using EasyCon2.Script.Assembly;

namespace EasyCon2.Script.Parsing.Statements
{
    class PushAll : Statement
    {
        public static readonly IStatementParser Parser = new StatementParser(Parse);

        public static Statement Parse(ParserArgument args)
        {
            if (args.Text.Equals("PUSHALL", StringComparison.OrdinalIgnoreCase))
                return new PushAll();
            return null;
        }

        protected override string _GetString(Formatter formatter)
        {
            return $"PUSHALL";
        }

        public override void Exec(Processor processor)
        {
            for (uint i = 1; i < Processor.RegisterCount; i++)
                processor.Stack.Push(processor.Register[i]);
        }

        public override void Assemble(Assembler assembler)
        {
            throw new AssembleException(ErrorMessage.NotSupported);
        }
    }

    class PopAll : Statement
    {
        public static readonly IStatementParser Parser = new StatementParser(Parse);

        public static Statement Parse(ParserArgument args)
        {
            if (args.Text.Equals("POPALL", StringComparison.OrdinalIgnoreCase))
                return new PopAll();
            return null;
        }

        protected override string _GetString(Formatter formatter)
        {
            return $"POPALL";
        }

        public override void Exec(Processor processor)
        {
            for (uint i = Processor.RegisterCount - 1; i >= 1; i--)
                processor.Register[i] = processor.Stack.Pop();
        }

        public override void Assemble(Assembler assembler)
        {
            throw new AssembleException(ErrorMessage.NotSupported);
        }
    }
}
