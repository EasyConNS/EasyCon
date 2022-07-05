namespace EasyScript.Parsing.Statements
{
    abstract class For : Statement
    {
        public override int IndentNext => 1;
        public readonly ValBase Count;
        public Next Next;

        public For(ValBase count)
        {
            Count = count;
        }

        protected virtual void Init(Processor processor)
        { }

        protected abstract bool Cond(Processor processor);

        protected virtual void Step(Processor processor)
        { }

        public override sealed void Exec(Processor processor)
        {
            if (processor.LoopStack.Count == 0 || processor.LoopStack.Peek() != this)
            {
                // entering
                processor.LoopStack.Push(this);
                Init(processor);
            }
            else
            {
                // looping
                Step(processor);
            }
            if (!Cond(processor))
                Break(processor);
        }

        public static void Break(Processor processor)
        {
            processor.PC = processor.LoopStack.Pop().Next.Address + 1;
        }

        public static void Continue(Processor processor)
        {
            processor.PC = processor.LoopStack.Peek().Next.Address;
        }
    }

    class For_Infinite : For
    {
        public For_Infinite()
            : base(0)
        { }

        protected override bool Cond(Processor processor)
        {
            return true;
        }

        protected override string _GetString(Formatter formatter)
        {
            return $"FOR";
        }

        public override void Assemble(Assembly.Assembler assembler)
        {
            assembler.Add(Assembly.Instructions.AsmFor.Create());
            assembler.ForMapping[this] = assembler.Last() as Assembly.Instructions.AsmFor;
        }
    }

    class For_Static : For
    {
        public For_Static(ValBase count)
            : base(count)
        { }

        protected override void Init(Processor processor)
        {
            processor.LoopTime[this] = 0;
            processor.LoopCount[this] = Count.Get(processor);
        }

        protected override bool Cond(Processor processor)
        {
            return processor.LoopTime[this] < processor.LoopCount[this];
        }

        protected override void Step(Processor processor)
        {
            processor.LoopTime[this]++;
        }

        protected override string _GetString(Formatter formatter)
        {
            return $"FOR {Count.GetCodeText(formatter)}";
        }

        public override void Assemble(Assembly.Assembler assembler)
        {
            if (Count is ValReg)
            {
                assembler.Add(Assembly.Instructions.AsmMov.Create(Assembly.Assembler.IReg, (int)((Count as ValReg).Index << 4)));
                assembler.Add(Assembly.Instructions.AsmStoreOp.Create(Assembly.Assembler.IReg));
            }
            else
                throw new Assembly.AssembleException(ErrorMessage.NotSupported);
            assembler.Add(Assembly.Instructions.AsmFor.Create());
            assembler.ForMapping[this] = assembler.Last() as Assembly.Instructions.AsmFor;
        }
    }

    class For_Full : For
    {
        public ValRegEx RegIter;
        public ValBase InitVal;

        public For_Full(ValRegEx regiter, ValBase initval, ValBase count)
            : base(count)
        {
            RegIter = regiter;
            InitVal = initval;
        }

        protected override void Init(Processor processor)
        {
            processor.Register[RegIter] = InitVal.Get(processor);
            processor.LoopCount[this] = Count.Get(processor);
        }

        protected override bool Cond(Processor processor)
        {
            return processor.Register[RegIter] < processor.LoopCount[this];
        }

        protected override void Step(Processor processor)
        {
            processor.Register[RegIter]++;
        }

        protected override string _GetString(Formatter formatter)
        {
            return $"FOR {RegIter.GetCodeText(formatter)} = {InitVal.GetCodeText(formatter)} TO {Count.GetCodeText(formatter)}";
        }

        public override void Assemble(Assembly.Assembler assembler)
        {
            if (RegIter is ValReg reg)
                assembler.Add(Assembly.Instructions.AsmMov.Create(reg.Index, InitVal));
            else 
                throw new Assembly.AssembleException(ErrorMessage.NotSupported);
            
            if (Count is ValReg countval)
            {
                uint e_val = countval.Index;
                e_val |= countval.Index << 4;
                assembler.Add(Assembly.Instructions.AsmMov.Create(Assembly.Assembler.IReg, (int)e_val));
                assembler.Add(Assembly.Instructions.AsmStoreOp.Create(Assembly.Assembler.IReg));
                assembler.Add(Assembly.Instructions.AsmFor.Create());
                assembler.ForMapping[this] = assembler.Last() as Assembly.Instructions.AsmFor;
            } 
            else
                throw new Assembly.AssembleException(ErrorMessage.NotSupported);
        }
    }

    class Next : Statement
    {
        public override int IndentThis => -1;
        public For For;

        protected override string _GetString(Formatter formatter)
        {
            return $"NEXT";
        }

        public override void Exec(Processor processor)
        {
            processor.PC = For.Address;
        }

        public override void Assemble(Assembly.Assembler assembler)
        {
            int val = 0;
            if (For.Count is ValInstant)
                val = (For.Count as ValInstant).Val;
            assembler.Add(Assembly.Instructions.AsmNext.Create(val));
            assembler.ForMapping[For].Next = assembler.Last() as Assembly.Instructions.AsmNext;
        }
    }

    abstract class LoopControl : Statement
    {
        public readonly ValInstant Level;
        protected bool _omitted;

        public LoopControl()
        {
            Level = 1;
            _omitted = true;
        }

        public LoopControl(ValInstant level)
        {
            Level = level;
            _omitted = false;
        }
    }

    class Break : LoopControl
    {
        public Break()
        { }

        public Break(ValInstant level)
            : base(level)
        { }

        protected override string _GetString(Formatter formatter)
        {
            return _omitted ? $"BREAK" : $"BREAK {Level.GetCodeText(formatter)}";
        }

        public override void Exec(Processor processor)
        {
            for (int i = 0; i < Level.Val - 1; i++)
                processor.LoopStack.Pop();
            For.Break(processor);
        }

        public override void Assemble(Assembly.Assembler assembler)
        {
            if (Level.Val <= 0)
                return;
            assembler.Add(Assembly.Instructions.AsmBreak.Create(0, Level.Val - 1));
        }
    }

    class Continue : LoopControl
    {
        public Continue()
        { }

        public Continue(ValInstant level)
            : base(level)
        { }

        protected override string _GetString(Formatter formatter)
        {
            return _omitted ? $"CONTINUE" : $"CONTINUE {Level.GetCodeText(formatter)}";
        }

        public override void Exec(Processor processor)
        {
            for (int i = 0; i < Level.Val - 1; i++)
                processor.LoopStack.Pop();
            For.Continue(processor);
        }

        public override void Assemble(Assembly.Assembler assembler)
        {
            if (Level.Val <= 0)
                return;
            assembler.Add(Assembly.Instructions.AsmContinue.Create(0, Level.Val - 1));
        }
    }
}
