using EasyScript.Parsing;

namespace EasyScript.Statements;

abstract class BinaryOp(ValReg regdst, ValBase value) : Statement
{
    protected abstract MetaOperator MetaInfo { get; }
    public readonly ValReg RegDst = regdst;
    public readonly ValBase Value = value;


    protected override string _GetString()
    {
        return $"{RegDst.GetCodeText()} {MetaInfo.Operator}= {Value.GetCodeText()}";
    }

    public override void Exec(Processor processor)
    {
        processor.Register[RegDst] = MetaInfo.Function(processor.Register[RegDst], Value.Get(processor));
    }

    public override void Assemble(Assembly.Assembler assembler)
    {
        if(RegDst is ValReg dest)
        {
            // TODO
            assembler.Add(Assembly.Instruction.CreateInstance(MetaInfo.InstructionType, dest.Reg, Value));
        }
    }
}

class Add : BinaryOp
{
    protected override MetaOperator MetaInfo => MetaOperator.Add;

    public Add(ValReg regdst, ValBase value)
        : base(regdst, value)
    { }
}

class Sub : BinaryOp
{
    protected override MetaOperator MetaInfo => MetaOperator.Sub;

    public Sub(ValReg regdst, ValBase value)
        : base(regdst, value)
    { }

    public override void Assemble(Assembly.Assembler assembler)
    {
        if (RegDst is ValReg dest)
        {
            if (Value is ValInstant val)
            {
                assembler.Add(Assembly.Instructions.AsmMov.Create(Assembly.Assembler.IReg, -val.Val));
                assembler.Add(Assembly.Instructions.AsmAdd.Create(dest.Reg, new ValReg(Assembly.Assembler.IReg)));
            }
            else if (Value is ValReg)
            {
                assembler.Add(Assembly.Instructions.AsmMov.Create(Assembly.Assembler.IReg, Value));
                assembler.Add(Assembly.Instructions.AsmNegative.Create(Assembly.Assembler.IReg));
                assembler.Add(Assembly.Instructions.AsmAdd.Create(dest.Reg, new ValReg(Assembly.Assembler.IReg)));
            }else
                throw new Assembly.AssembleException(ErrorMessage.NotSupported);
        }
        else
            throw new Assembly.AssembleException(ErrorMessage.NotSupported);
    }
}

class Mul : BinaryOp
{
    protected override MetaOperator MetaInfo => MetaOperator.Mul;

    public Mul(ValReg regdst, ValBase value)
        : base(regdst, value)
    { }
}

class Div : BinaryOp
{
    protected override MetaOperator MetaInfo => MetaOperator.Div;

    public Div(ValReg regdst, ValBase value)
        : base(regdst, value)
    { }
}

class RoundDiv : BinaryOp
{
    protected override MetaOperator MetaInfo => MetaOperator.RoundDiv;

    public RoundDiv(ValReg regdst, ValBase value)
        : base(regdst, value)
    { }

    public override void Assemble(Assembly.Assembler assembler)
    {
        throw new Assembly.AssembleException(ErrorMessage.NotSupported);
    }
}

class Mod : BinaryOp
{
    protected override MetaOperator MetaInfo => MetaOperator.Mod;

    public Mod(ValReg regdst, ValBase value)
        : base(regdst, value)
    { }
}

class And : BinaryOp
{
    protected override MetaOperator MetaInfo => MetaOperator.And;

    public And(ValReg regdst, ValBase value)
        : base(regdst, value)
    { }
}

class Or : BinaryOp
{
    protected override MetaOperator MetaInfo => MetaOperator.Or;

    public Or(ValReg regdst, ValBase value)
        : base(regdst, value)
    { }
}

class Xor : BinaryOp
{
    protected override MetaOperator MetaInfo => MetaOperator.Xor;

    public Xor(ValReg regdst, ValBase value)
        : base(regdst, value)
    { }
}

class ShiftLeft : BinaryOp
{
    protected override MetaOperator MetaInfo => MetaOperator.LShift;

    public ShiftLeft(ValReg regdst, ValBase value)
        : base(regdst, value)
    { }
}

class ShiftRight : BinaryOp
{
    protected override MetaOperator MetaInfo => MetaOperator.RShift;

    public ShiftRight(ValReg regdst, ValBase value)
        : base(regdst, value)
    { }
}
