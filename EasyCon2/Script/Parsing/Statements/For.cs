using System.Text.RegularExpressions;

namespace EasyCon2.Script.Parsing.Statements
{
    abstract class For : Statement
    {
        public static IStatementParser Parser = new StatementParser(Parse);
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

        public static Statement Parse(ParserArgument args)
        {
            Match m;
            if (args.Text.Equals("for", StringComparison.OrdinalIgnoreCase))
                return new For_Infinite();
            m = Regex.Match(args.Text, $@"^for\s+{Formats.ValueEx}$", RegexOptions.IgnoreCase);
            if (m.Success)
                return new For_Static(args.Formatter.GetValueEx(m.Groups[1].Value));
            m = Regex.Match(args.Text, $@"^for\s+{Formats.RegisterEx}\s*=\s*{Formats.ValueEx}\s*to\s*{Formats.ValueEx}$", RegexOptions.IgnoreCase);
            if (m.Success)
                return new For_Full(Formatter.GetRegEx(m.Groups[1].Value, true), args.Formatter.GetValueEx(m.Groups[2].Value), args.Formatter.GetValueEx(m.Groups[3].Value));
            return null;
        }

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
            else if (Count is ValReg32)
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
            if (RegIter is ValReg32)
                throw new Assembly.AssembleException(ErrorMessage.NotSupported);
            assembler.Add(Assembly.Instructions.AsmMov.Create(RegIter.Index, InitVal));
            uint e_val = RegIter.Index;
            if (Count is ValReg)
                e_val |= (Count as ValReg).Index << 4;
            else if (Count is ValReg32)
                throw new Assembly.AssembleException(ErrorMessage.NotSupported);
            assembler.Add(Assembly.Instructions.AsmMov.Create(Assembly.Assembler.IReg, (int)e_val));
            assembler.Add(Assembly.Instructions.AsmStoreOp.Create(Assembly.Assembler.IReg));
            assembler.Add(Assembly.Instructions.AsmFor.Create());
            assembler.ForMapping[this] = assembler.Last() as Assembly.Instructions.AsmFor;
        }
    }

    class Next : Statement
    {
        public static readonly IStatementParser Parser = new StatementParser(Parse);
        public override int IndentThis => -1;
        public For For;

        public static Statement Parse(ParserArgument args)
        {
            if (args.Text.Equals("next", StringComparison.OrdinalIgnoreCase))
                return new Next();
            return null;
        }

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
        public static readonly IStatementParser Parser = new StatementParser(Parse);

        public Break()
        { }

        public Break(ValInstant level)
            : base(level)
        { }

        public static Statement Parse(ParserArgument args)
        {
            if (args.Text.Equals("break", StringComparison.OrdinalIgnoreCase))
                return new Break();
            var m = Regex.Match(args.Text, $@"^break\s+{Formats.Instant}$", RegexOptions.IgnoreCase);
            if (m.Success)
                return new Break(args.Formatter.GetInstant(m.Groups[1].Value, true));
            return null;
        }

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
        public static readonly IStatementParser Parser = new StatementParser(Parse);

        public Continue()
        { }

        public Continue(ValInstant level)
            : base(level)
        { }

        public static Statement Parse(ParserArgument args)
        {
            if (args.Text.Equals("continue", StringComparison.OrdinalIgnoreCase))
                return new Continue();
            var m = Regex.Match(args.Text, $@"^continue\s+{Formats.Instant}$", RegexOptions.IgnoreCase);
            if (m.Success)
                return new Continue(args.Formatter.GetInstant(m.Groups[1].Value, true));
            return null;
        }

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
