using EasyScript.Parsing;

namespace EasyScript.Statements
{
    class FunctionStmt : Statement
    {
        public override int IndentNext => 1;
        public readonly string Label;
        public EndFuncStat Ret = null;

        public FunctionStmt(string lbl)
        {
            Label = lbl;
        }

        public override void Assemble(Assembly.Assembler assembler)
        {
            assembler.Add(Assembly.Instructions.AsmBranch.Create());
            assembler.FunctionMapping[Label] = assembler.Last() as Assembly.Instructions.AsmBranch;
            assembler.Add(Assembly.Instructions.AsmEmpty.Create());
            assembler.CallMapping[Label] = assembler.Last() as Assembly.Instructions.AsmEmpty;
        }

        public override void Exec(Processor processor)
        {
            processor.PC = Ret.Address + 1;
        }

        protected override string _GetString()
        {
            return $"FUNC {Label}";
        }
    }

    class CallStat : Statement
    {
        public readonly string Label;
        public FunctionStmt Func = null;

        public CallStat(string lbl)
        {
            Label = lbl;
        }

        public override void Assemble(Assembly.Assembler assembler)
        {
            var callfunc = assembler.CallMapping.GetValueOrDefault(Label, null);
            assembler.Add(Assembly.Instructions.AsmCall.Create(callfunc));
        }

        public override void Exec(Processor processor)
        {
            processor.Call(Label);
        }

        protected override string _GetString()
        {
            return $"CALL {Label}";
        }
    }

    class EndFuncStat : Statement
    {
        public override int IndentThis => -1;
        public string Label;
        protected override string _GetString()
        {
            return "ENDFUNC";
        }

        public override void Exec(Processor processor)
        {
            processor.RetrunCall();
        }

        public override void Assemble(Assembly.Assembler assembler)
        {
            assembler.Add(Assembly.Instructions.AsmReturn.Create(0));
            assembler.Add(Assembly.Instructions.AsmEmpty.Create());
            assembler.FunctionMapping[this.Label].Target = assembler.Last();
        }
    }

    class ReturnStat : Statement
    {
        public string Label;
        protected override string _GetString()
        {
            return "RETURN";
        }

        public override void Exec(Processor processor)
        {
            processor.RetrunCall();
        }

        public override void Assemble(Assembly.Assembler assembler)
        {
            throw new Assembly.AssembleException(ErrorMessage.NotImplemented);
        }
    }
}
