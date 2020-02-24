using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyCon.Script.Parsing
{
    abstract class ValBase
    {
        public abstract int Evaluate(Processor processor);
        public abstract string GetCodeText(Formats.Formatter formatter);

        public static implicit operator ValBase(int val)
        {
            return new ValInstant(val);
        }
    }

    class ValInstant : ValBase
    {
        public readonly int Val;
        public readonly string Text;

        public ValInstant(int val)
        {
            Val = val;
            Text = val.ToString();
        }

        public ValInstant(int val, string text)
        {
            Val = val;
            Text = text;
        }

        public override int Evaluate(Processor processor)
        {
            return Val;
        }

        public override string GetCodeText(Formats.Formatter formatter)
        {
            return Text;
        }

        public static implicit operator ValInstant(int val)
        {
            return new ValInstant(val);
        }
    }

    class ValReg : ValBase
    {
        public readonly uint Index;

        public ValReg(uint reg)
        {
            Index = reg;
        }

        public override int Evaluate(Processor processor)
        {
            return processor.Register[Index];
        }

        public override string GetCodeText(Formats.Formatter formatter)
        {
            return formatter.GetRegText(Index);
        }
    }
}
