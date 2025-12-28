using EasyScript.Parsing;

namespace EasyScript.Statements;

public class Meta
{
    public readonly Type StatementType;
    public readonly Type InstructionType;
    public readonly string Operator;
    public readonly Func<int, int, int> Function;
    public readonly bool OnlyInstant;

    public Meta(Type statementType, Type instructionType, string op, Func<int, int, int> function, bool onlyInstant = false)
    {
        StatementType = statementType;
        InstructionType = instructionType;
        Operator = op;
        Function = function;
        OnlyInstant = onlyInstant;
    }
}
abstract class BinaryOp : Statement
{
    protected abstract Meta MetaInfo { get; }
    public readonly ValReg RegDst;
    public readonly ValBase Value;

    public BinaryOp(ValReg regdst, ValBase value)
    {
        RegDst = regdst;
        Value = value;
    }


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
    static readonly Meta _Meta = new(typeof(Add), typeof(Assembly.Instructions.AsmAdd), "+", (a, b) => a + b);
    protected override Meta MetaInfo => _Meta;

    public Add(ValReg regdst, ValBase value)
        : base(regdst, value)
    { }
}

class Sub : BinaryOp
{
    static readonly Meta _Meta = new(typeof(Sub), null, "-", (a, b) => a - b);
    protected override Meta MetaInfo => _Meta;

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
    static readonly Meta _Meta = new(typeof(Mul), typeof(Assembly.Instructions.AsmMul), "*", (a, b) => a * b);
    protected override Meta MetaInfo => _Meta;

    public Mul(ValReg regdst, ValBase value)
        : base(regdst, value)
    { }
}

class Div : BinaryOp
{
    static readonly Meta _Meta = new(typeof(Div), typeof(Assembly.Instructions.AsmDiv), "/", (a, b) => a / b);
    protected override Meta MetaInfo => _Meta;

    public Div(ValReg regdst, ValBase value)
        : base(regdst, value)
    { }
}

class RoundDiv : BinaryOp
{
    static readonly Meta _Meta = new(typeof(RoundDiv), typeof(Assembly.Instructions.AsmDiv), @"\", (a, b) => (int)Math.Round((double)a / b) );
    protected override Meta MetaInfo => _Meta;

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
    static readonly Meta _Meta = new(typeof(Mod), typeof(Assembly.Instructions.AsmMod), "%", (a, b) => a % b);
    protected override Meta MetaInfo => _Meta;

    public Mod(ValReg regdst, ValBase value)
        : base(regdst, value)
    { }
}

class And : BinaryOp
{
    static readonly Meta _Meta = new(typeof(And), typeof(Assembly.Instructions.AsmAnd), "&", (a, b) => a & b);
    protected override Meta MetaInfo => _Meta;

    public And(ValReg regdst, ValBase value)
        : base(regdst, value)
    { }
}

class Or : BinaryOp
{
    static readonly Meta _Meta = new(typeof(Or), typeof(Assembly.Instructions.AsmOr), "|", (a, b) => a | b);
    protected override Meta MetaInfo => _Meta;

    public Or(ValReg regdst, ValBase value)
        : base(regdst, value)
    { }
}

class Xor : BinaryOp
{
    static readonly Meta _Meta = new(typeof(Xor), typeof(Assembly.Instructions.AsmXor), "^", (a, b) => a ^ b);
    protected override Meta MetaInfo => _Meta;

    public Xor(ValReg regdst, ValBase value)
        : base(regdst, value)
    { }
}

class ShiftLeft : BinaryOp
{
    static readonly Meta _Meta = new(typeof(ShiftLeft), typeof(Assembly.Instructions.AsmShiftLeft), "<<", (a, b) => a << b, true);
    protected override Meta MetaInfo => _Meta;

    public ShiftLeft(ValReg regdst, ValBase value)
        : base(regdst, value)
    { }
}

class ShiftRight : BinaryOp
{
    static readonly Meta _Meta = new(typeof(ShiftRight), typeof(Assembly.Instructions.AsmShiftRight), ">>", (a, b) => a >> b, true);
    protected override Meta MetaInfo => _Meta;

    public ShiftRight(ValReg regdst, ValBase value)
        : base(regdst, value)
    { }
}
