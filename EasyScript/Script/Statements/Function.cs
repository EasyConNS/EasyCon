namespace EasyScript.Parsing.Statements
{
    class Function : Statement
    {
        public override int IndentNext => 1;
        public readonly string Label;
        public ReturnStat Ret = null;

        public Function(string lbl)
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
            processor.FunctionDefinitionStack.Push(new FunctionDefinition(Label));
        }

        protected override string _GetString(Formatter formatter)
        {
            return $"FUNC {Label}";
        }
    }

    class CallStat : ControlStatement
    {
        public readonly string Label;
        public Function Func = null;

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
            if(processor.FunctionDefinitions.TryGetValue(Label, out var func))
            {
                processor.CallStack.Push(processor.PC);
                processor.PC = 0;

                var scriptor = new Scripter();
                scriptor.load(func.Body(), processor.extVars);
                processor = scriptor.explain(processor);
                processor.PC = processor.CallStack.Pop();
            }
            else
            {
                throw new ScriptException($"Function {Label} not found", Address);
            }
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
            return "ENDFUNC";
        }

        public override void Exec(Processor processor)
        {
            var f = processor.FunctionDefinitionStack.Pop();

            processor.FunctionDefinitions.Add(f.Label, f);
        }

        public override void Assemble(Assembly.Assembler assembler)
        {
            assembler.Add(Assembly.Instructions.AsmReturn.Create(0));
            assembler.Add(Assembly.Instructions.AsmEmpty.Create());
            assembler.FunctionMapping[this.Label].Target = assembler.Last();
        }
    }

    internal class FunctionDefinition
    {
        public readonly string Label;

        public readonly List<string> Statements = new ();

        public FunctionDefinition(string lbl)
        {
            Label = lbl;
        }

        public void Push(ParserArgument statement)
        {
            Statements.Add(statement.Text);
        }

        public string Body()
        {
            return string.Join("\n", Statements);
        }
    };
}
