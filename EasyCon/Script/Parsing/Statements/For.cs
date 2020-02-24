using EasyCon.Script.Assembly;
using EasyCon.Script.Assembly.Instructions;
using EasyCon.Script.Parsing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EasyCon.Script.Parsing.Statements
{
    abstract class For : Statement
    {
        public static IStatementParser Parser = new StatementParser(Parse);
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
            m = Regex.Match(args.Text, $@"^for\s+{Formats.Value}$", RegexOptions.IgnoreCase);
            if (m.Success)
                return new For_Static(args.Formatter.GetValue(m.Groups[1].Value));
            m = Regex.Match(args.Text, $@"^for\s+{Formats.Register}\s*=\s*{Formats.Value}\s*to\s*{Formats.Value}$", RegexOptions.IgnoreCase);
            if (m.Success)
                return new For_Full(args.Formatter.GetReg(m.Groups[1].Value, true).Index, args.Formatter.GetValue(m.Groups[2].Value), args.Formatter.GetValue(m.Groups[3].Value));
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

        protected override string _GetString(Formats.Formatter formatter)
        {
            return $"FOR";
        }

        public override void Assemble(Assembler assembler)
        {
            assembler.Add(AsmFor.Create());
            assembler.ForMapping[this] = assembler.Last() as AsmFor;
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
            processor.LoopCount[this] = Count.Evaluate(processor);
        }

        protected override bool Cond(Processor processor)
        {
            return processor.LoopTime[this] < processor.LoopCount[this];
        }

        protected override void Step(Processor processor)
        {
            processor.LoopTime[this]++;
        }

        protected override string _GetString(Formats.Formatter formatter)
        {
            return $"FOR {Count.GetCodeText(formatter)}";
        }

        public override void Assemble(Assembler assembler)
        {
            if (Count is ValReg)
            {
                assembler.Add(AsmMov.Create(Assembler.IReg, (int)((Count as ValReg).Index << 4)));
                assembler.Add(AsmStoreOp.Create(Assembler.IReg));
            }
            assembler.Add(AsmFor.Create());
            assembler.ForMapping[this] = assembler.Last() as AsmFor;
        }
    }

    class For_Full : For
    {
        public uint RegIter;
        public ValBase InitVal;

        public For_Full(uint regiter, ValBase initval, ValBase count)
            : base(count)
        {
            RegIter = regiter;
            InitVal = initval;
        }

        protected override void Init(Processor processor)
        {
            processor.Register[RegIter] = (short)InitVal.Evaluate(processor);
            processor.LoopCount[this] = Count.Evaluate(processor);
        }

        protected override bool Cond(Processor processor)
        {
            return processor.Register[RegIter] < processor.LoopCount[this];
        }

        protected override void Step(Processor processor)
        {
            processor.Register[RegIter]++;
        }

        protected override string _GetString(Formats.Formatter formatter)
        {
            return $"FOR {formatter.GetRegText(RegIter)} = {InitVal.GetCodeText(formatter)} TO {Count.GetCodeText(formatter)}";
        }

        public override void Assemble(Assembler assembler)
        {
            assembler.Add(AsmMov.Create(RegIter, InitVal));
            uint e_val = RegIter;
            if (Count is ValReg)
                e_val |= (Count as ValReg).Index << 4;
            assembler.Add(AsmMov.Create(Assembler.IReg, (int)e_val));
            assembler.Add(AsmStoreOp.Create(Assembler.IReg));
            assembler.Add(AsmFor.Create());
            assembler.ForMapping[this] = assembler.Last() as AsmFor;
        }
    }

    class Next : Statement
    {
        public static readonly IStatementParser Parser = new StatementParser(Parse);
        public For For;

        public static Statement Parse(ParserArgument args)
        {
            if (args.Text.Equals("next", StringComparison.OrdinalIgnoreCase))
                return new Next();
            return null;
        }

        protected override string _GetString(Formats.Formatter formatter)
        {
            return $"NEXT";
        }

        public override void Exec(Processor processor)
        {
            processor.PC = For.Address;
        }

        public override void Assemble(Assembler assembler)
        {
            int val = 0;
            if (For.Count is ValInstant)
                val = (For.Count as ValInstant).Val;
            assembler.Add(AsmNext.Create(val));
            assembler.ForMapping[For].Next = assembler.Last() as AsmNext;
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
            Match m;
            if (args.Text.Equals("break", StringComparison.OrdinalIgnoreCase))
                return new Break();
            m = Regex.Match(args.Text, $@"^break\s+{Formats.Instant}$", RegexOptions.IgnoreCase);
            if (m.Success)
                return new Break(args.Formatter.GetInstant(m.Groups[1].Value, true));
            return null;
        }

        protected override string _GetString(Formats.Formatter formatter)
        {
            return _omitted ? $"BREAK" : $"BREAK {Level.GetCodeText(formatter)}";
        }

        public override void Exec(Processor processor)
        {
            for (int i = 0; i < Level.Val - 1; i++)
                processor.LoopStack.Pop();
            For.Break(processor);
        }

        public override void Assemble(Assembler assembler)
        {
            if (Level.Val <= 0)
                return;
            assembler.Add(AsmBreak.Create(0, Level.Val - 1));
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
            Match m;
            if (args.Text.Equals("continue", StringComparison.OrdinalIgnoreCase))
                return new Continue();
            m = Regex.Match(args.Text, $@"^continue\s+{Formats.Instant}$", RegexOptions.IgnoreCase);
            if (m.Success)
                return new Continue(args.Formatter.GetInstant(m.Groups[1].Value, true));
            return null;
        }

        protected override string _GetString(Formats.Formatter formatter)
        {
            return _omitted ? $"CONTINUE" : $"CONTINUE {Level.GetCodeText(formatter)}";
        }

        public override void Exec(Processor processor)
        {
            for (int i = 0; i < Level.Val - 1; i++)
                processor.LoopStack.Pop();
            For.Continue(processor);
        }

        public override void Assemble(Assembler assembler)
        {
            if (Level.Val <= 0)
                return;
            assembler.Add(AsmContinue.Create(0, Level.Val - 1));
        }
    }
}
