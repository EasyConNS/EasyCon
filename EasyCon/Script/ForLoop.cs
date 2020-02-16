using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EasyCon.Script
{
    abstract class ForLoop : Statement
    {
        public readonly Next Next;

        protected ForLoop()
        {
            Next = new Next(this);
        }

        protected virtual void Init(Processor processor)
        { }

        protected abstract bool Cond(Processor processor);

        protected virtual void Step(Processor processor)
        { }

        public static Statement TryCompile(string text)
        {
            Match m;
            if (text.Equals("for", StringComparison.OrdinalIgnoreCase))
                return new ForLoopInfinite();
            m = Regex.Match(text, @"^for\s+(\d+)$", RegexOptions.IgnoreCase);
            if (m.Success)
                return new ForLoopStatic(int.Parse(m.Groups[1].Value));
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

        public void Break(Processor processor)
        {
            processor.LoopStack.Pop();
            processor.PC = Next.Address + 1;
        }
    }

    class ForLoopInfinite : ForLoop
    {
        public ForLoopInfinite()
        { }

        protected override bool Cond(Processor processor)
        {
            return true;
        }

        protected override string _ToString()
        {
            return $"FOR";
        }
    }

    class ForLoopStatic : ForLoop
    {
        public readonly int Times;

        public ForLoopStatic(int times)
        {
            Times = times;
        }

        protected override void Init(Processor processor)
        {
            processor.LoopVar[this] = 0;
        }

        protected override bool Cond(Processor processor)
        {
            return processor.LoopVar[this] < Times;
        }

        protected override void Step(Processor processor)
        {
            processor.LoopVar[this]++;
        }


        protected override string _ToString()
        {
            return $"FOR {Times}";
        }
    }

    class Next : Statement
    {
        public readonly ForLoop ForLoop;

        public Next(ForLoop forLoop)
        {
            ForLoop = forLoop;
        }

        protected override string _ToString()
        {
            return $"NEXT";
        }

        public override void Exec(Processor processor)
        {
            processor.PC = ForLoop.Address;
        }
    }
}
