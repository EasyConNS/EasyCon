using System.Text.RegularExpressions;

namespace EasyCon2.Script.Parsing.Statements
{
    class Function : Statement
    {
        public override int IndentNext => 1;
        public readonly string Label;
        public ReturnStat Ret = null;

        public static IStatementParser Parser = new StatementParser(Parse);

        public static Statement Parse(ParserArgument args)
        {
            var m = Regex.Match(args.Text, @"^func\s+(\D[\d\p{L}_]+)$", RegexOptions.IgnoreCase);
            if (m.Success)
                return new Function(m.Groups[1].Value);
            m = Regex.Match(args.Text, @"^call\s+(\D[\d\p{L}_]+)$", RegexOptions.IgnoreCase);
            if (m.Success)
                return new CallStat(m.Groups[1].Value);
            if (args.Text.Equals("ret", StringComparison.OrdinalIgnoreCase))
                return new ReturnStat();
            return null;
        }

        public Function(string lbl)
        {
            Label = lbl;
        }

        public override void Assemble(Assembly.Assembler assembler)
        {
            assembler.Add(Assembly.Instructions.AsmBranch.Create());
            assembler.FunctionMapping[this.Label] = assembler.Last() as Assembly.Instructions.AsmBranch;
            assembler.Add(Assembly.Instructions.AsmEmpty.Create());
            assembler.CallMapping[this.Label] = assembler.Last() as Assembly.Instructions.AsmEmpty;
        }

        public override void Exec(Processor processor)
        {
            processor.PC = Ret.Address + 1;
        }

        protected override string _GetString(Formatter formatter)
        {
            return $"FUNC {Label}";
        }
    }

    class CallStat : Statement
    {
        public readonly string Label;
        public Function Func = null;

        public CallStat(string lbl)
        {
            Label = lbl;
        }

        public override void Assemble(Assembly.Assembler assembler)
        {
            assembler.Add(Assembly.Instructions.AsmCall.Create());
            assembler.Add(Assembly.Instructions.AsmLabel.Create(Label));
        }

        public override void Exec(Processor processor)
        {
            processor.CallStack.Push(processor.PC);
            processor.PC = Func.Address + 1;
        }

        protected override string _GetString(Formatter formatter)
        {
            return $"CALL {Label}";
        }
    }

    class ReturnStat : Statement
    {
        public override int IndentThis => -1;
        public string Label;
        protected override string _GetString(Formatter formatter)
        {
            return "RET";
        }

        public override void Exec(Processor processor)
        {
            processor.PC = processor.CallStack.Pop();
        }

        public override void Assemble(Assembly.Assembler assembler)
        {
            assembler.Add(Assembly.Instructions.AsmReturn.Create(0));
            assembler.Add(Assembly.Instructions.AsmEmpty.Create());
            assembler.FunctionMapping[this.Label].Target = assembler.Last();
        }
    }
}
