namespace EasyCon2.Script.Parsing
{
    // base of valuetype
    abstract class ValBase
    {
        public abstract int Get(Processor processor);

        public virtual void Set(Processor processor, int value)
        {
            throw new InvalidOperationException();
        }

        public abstract string GetCodeText(Formatter formatter);

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

        public override int Get(Processor processor)
        {
            return Val;
        }

        public override string GetCodeText(Formatter formatter)
        {
            return Text;
        }

        public static implicit operator ValInstant(int val)
        {
            return new ValInstant(val);
        }
    }

    // variable, either register or external
    abstract class ValVar : ValBase
    {
        public abstract override void Set(Processor processor, int value);
    }

    // register, either 16 or 32 bits
    abstract class ValRegEx : ValVar
    {
        public readonly uint Index;

        public ValRegEx(uint reg)
        {
            Index = reg;
        }

        public override void Set(Processor processor, int value)
        {
            processor.Register[this] = value;
        }
    }

    // 16-bit register
    class ValReg : ValRegEx
    {
        public ValReg(uint reg)
            : base(reg)
        { }

        public override int Get(Processor processor)
        {
            return processor.Register[Index];
        }

        public override string GetCodeText(Formatter formatter)
        {
            return Formatter.GetRegText(Index);
        }
    }

    // 32-bit combined register
    class ValReg32 : ValRegEx
    {
        public ValReg32(uint reg)
            : base(reg)
        { }

        public override int Get(Processor processor)
        {
            return processor.Register.GetReg32(Index);
        }

        public override string GetCodeText(Formatter formatter)
        {
            return Formatter.GetReg32Text(Index);
        }
    }

    // external variable 
    class ValExtVar : ValVar
    {
        public readonly ExternalVariable Var;

        public ValExtVar(ExternalVariable var)
        {
            Var = var;
        }

        public override int Get(Processor processor)
        {
            return Var.Get();
        }

        public override void Set(Processor processor, int value)
        {
            Var.Set(value);
        }

        public override string GetCodeText(Formatter formatter)
        {
            return Formatter.GetExtVarText(Var.Name);
        }
    }
}
