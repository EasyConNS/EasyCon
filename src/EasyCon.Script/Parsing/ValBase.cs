namespace EasyScript.Parsing
{
    // base of valuetype
    abstract class ValBase
    {
        public abstract int Get(Processor processor);

        public virtual void Set(Processor processor, int value)
        {
            throw new InvalidOperationException();
        }

        public abstract string GetCodeText();

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

        public override int Get(Processor _)
        {
            return Val;
        }

        public override string GetCodeText()
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

    // register variable, either 16 or 32 bits
    class ValRegEx : ValVar
    {
        public readonly string Tag;

        public ValRegEx(string reg)
        {
            Tag = reg;
        }

        public ValRegEx(uint reg)
        {
            Tag = reg.ToString();
        }

        public override void Set(Processor processor, int value)
        {
            processor.Register[this] = value;
        }

        public override int Get(Processor processor)
        {
            return processor.Register[this];
        }

        public override string GetCodeText()
        {
            return $"${Tag}";
        }
    }

    // 16-bit register Index variable
    class ValReg(uint reg) : ValRegEx(reg)
    {
        public readonly uint Index = reg;
    }

    // external variable 
    class ValExtVar(ExternalVariable var) : ValVar
    {
        public readonly ExternalVariable Var = var;

        public override int Get(Processor _)
        {
            return Var.Get();
        }

        public override void Set(Processor processor, int value)
        {
            throw new InvalidOperationException();
        }

        public override string GetCodeText()
        {
            return $"@{Var.Name}";
        }
    }
}
