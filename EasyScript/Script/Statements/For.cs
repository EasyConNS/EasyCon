namespace EasyScript.Parsing.Statements
{
    abstract class For : ControlStatement
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
            if (processor.ControlStack.Count == 0 || processor.ControlStack.Peek().Address != Address)
            {
                // entering
                processor.ControlStack.Push(this);
                Init(processor);
            }
            else
            {
                // looping
                Step(processor);
            }
            if (!Cond(processor))
            { 
                Break(processor); 
            }
        }

        public static void Break(Processor processor)
        {
            var ctrl = processor.ControlStack.Peek();
            if (ctrl is For forCmd)
            {
                processor.PC = forCmd.Next.Address + 1;
                processor.ControlStack.Pop();
            }    
        }

        public static void Continue(Processor processor)
        {
            var ctrl = processor.ControlStack.Peek();
            if (ctrl is For forCmd)
            {
                processor.PC = forCmd.Next.Address;
            }
            
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
            processor.LoopTime[this.Address] = 0;
            processor.LoopCount[this.Address] = Count.Get(processor);
        }

        protected override bool Cond(Processor processor)
        {
            return processor.LoopTime[this.Address] < processor.LoopCount[this.Address];
        }

        protected override void Step(Processor processor)
        {
            processor.LoopTime[this.Address]++;
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
            else if (Count is ValRegEx)
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
            processor.LoopCount[this.Address] = Count.Get(processor);
        }

        protected override bool Cond(Processor processor)
        {
            return processor.Register[RegIter] < processor.LoopCount[this.Address];
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
        public For For = null;

        protected override string _GetString(Formatter formatter)
        {
            return $"NEXT";
        }

        public override void Exec(Processor processor)
        {
            if (For == null)
            {
                if (processor.ControlStack.Peek() is For forCmd)
                {
                    For = forCmd;
                    For.Next = this;
                }
                else
                {
                    throw new ParseException($"NEXT without FOR", processor.PC);
                }
            }
            processor.PC = For.Address - 1;
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
            int i = Level.Val - 1;
            while (i > 0) 
            {
                try
                {
                    if (processor.ControlStack.Pop() is For)
                    {
                        i--;
                    }
                }
                catch (InvalidOperationException)
                {
                    throw new ParseException($"BREAK 层数过多", processor.PC);
                }
            }
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
            int i = Level.Val - 1;
            while (i > 0)
            {
                try
                {
                    if (processor.ControlStack.Pop() is For)
                    {
                        i--;
                    }
                }
                catch (InvalidOperationException)
                {
                    throw new ParseException($"BREAK 层数过多", processor.PC);
                }
            }
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
