namespace EasyScript.Parsing;

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

    public static implicit operator int(ValInstant v)
    {
        return v.Val;
    }
}

// register variable, either 16 or 32 bits
class ValReg : ValBase
{
    public readonly string Tag;
    public readonly uint Reg;

    public ValReg(string tag)
    {
        Tag = tag;
        Reg = 0;
    }

    public ValReg(uint reg)
    {
        Tag = reg.ToString();
        Reg = reg;
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

// external variable 
class ValExtVar(ExternalVariable var) : ValBase
{
    public readonly ExternalVariable Var = var;

    public override int Get(Processor _)
    {
        return Var.Get();
    }

    public override string GetCodeText()
    {
        return $"@{Var.Name}";
    }
}
