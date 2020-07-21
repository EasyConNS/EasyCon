using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyCon.Script.Parsing
{
    // base of valuetype
    abstract class ValBase
    {
        public abstract int Evaluate(Processor processor);
        public abstract string GetCodeText(Formats.Formatter formatter);

        public static implicit operator ValBase(int val)
        {
            return new ValInstant(val);
        }
    }

    // instant number, including constant
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

    // register, either 16 or 32 bits
    abstract class ValRegEx : ValBase
    {
        public readonly uint Index;

        public ValRegEx(uint reg)
        {
            Index = reg;
        }
    }

    // 16-bit register
    class ValReg : ValRegEx
    {
        public ValReg(uint reg)
            : base(reg)
        { }

        public override int Evaluate(Processor processor)
        {
            return processor.Register[Index];
        }

        public override string GetCodeText(Formats.Formatter formatter)
        {
            return formatter.GetRegText(Index);
        }
    }

    // 32-bit combined register
    class ValReg32 : ValRegEx
    {
        public ValReg32(uint reg)
            : base(reg)
        { }

        public override int Evaluate(Processor processor)
        {
            return processor.Register.GetReg32(Index);
        }

        public override string GetCodeText(Formats.Formatter formatter)
        {
            return formatter.GetReg32Text(Index);
        }
    }

    // imglabel 
    class ValImglabel : ValRegEx
    {
        public readonly string Text;
        public readonly int Val;
        public ValImglabel(uint reg,string text,int val)
            : base(reg)
        { 
            Text = text;
            Val = val;
        }

        public override int Evaluate(Processor processor)
        {
            return Val;
        }

        public override string GetCodeText(Formats.Formatter formatter)
        {
            return Text;
        }
    }
}
