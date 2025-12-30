using EasyScript.Parsing;

namespace EasyScript.Statements;

class ExpressionStmt(VariableExpr regdst, ExprBase value) : Statement
{
    protected readonly VariableExpr RegDst = regdst;
    protected readonly ExprBase Value = value;

    protected override string _GetString()
    {
        return $"{RegDst.GetCodeText()} = {Value.GetCodeText()}";
    }

    public override void Exec(Processor processor)
    {
        RegDst.Set(processor, BinaryExpression.Rewrite(Value).Get(processor));
    }

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    if (RegDst is VariableExpr reg)
    //    {
    //        // assembler.Add(Assembly.Instructions.AsmMov.Create(reg.Index, ValueLeft));
    //        // if (OpMeta != null)
    //        // {
    //        //     if (OpMeta.Operator != "-")
    //        //     {
    //        //         assembler.Add(Assembly.Instruction.CreateInstance(OpMeta.InstructionType, reg.Index, ValueRight));
    //        //     }
    //        //     else
    //        //     {
    //        //         if (ValueRight is ValInstant)
    //        //         {
    //        //             assembler.Add(Assembly.Instructions.AsmMov.Create(Assembly.Assembler.IReg, -(ValueRight as ValInstant).Val));
    //        //             assembler.Add(Assembly.Instructions.AsmAdd.Create(reg.Index, new ValReg(Assembly.Assembler.IReg)));
    //        //         }
    //        //         else if (ValueRight is ValReg)
    //        //         {
    //        //             assembler.Add(Assembly.Instructions.AsmMov.Create(Assembly.Assembler.IReg, ValueRight));
    //        //             assembler.Add(Assembly.Instructions.AsmNegative.Create(Assembly.Assembler.IReg));
    //        //             assembler.Add(Assembly.Instructions.AsmAdd.Create(reg.Index, new ValReg(Assembly.Assembler.IReg)));
    //        //         }

    //        //         else
    //        //             throw new Assembly.AssembleException(ErrorMessage.NotSupported);
    //        //     }
    //        // }
    //        throw new Assembly.AssembleException(ErrorMessage.NotSupported);
    //    }
    //    else
    //    {
    //        throw new Assembly.AssembleException(ErrorMessage.NotSupported);
    //    }
    //}
}
#region binaryOp操作
abstract class BinaryOp(VariableExpr regdst, ExprBase value) : Statement
{
    protected abstract MetaOperator MetaInfo { get; }
    public readonly VariableExpr RegDst = regdst;
    public readonly ExprBase Value = value;


    protected override string _GetString()
    {
        return $"{RegDst.GetCodeText()} {MetaInfo.Operator}= {Value.GetCodeText()}";
    }

    public override void Exec(Processor processor)
    {
        processor.Register[RegDst] = MetaInfo.Function(processor.Register[RegDst], Value.Get(processor));
    }

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    if(RegDst is VariableExpr dest)
    //    {
    //        // TODO
    //        assembler.Add(Assembly.Instruction.CreateInstance(MetaInfo.InstructionType, dest.Reg, Value));
    //    }
    //}
}

class Add(VariableExpr regdst, ExprBase value) : BinaryOp(regdst, value)
{
    protected override MetaOperator MetaInfo => MetaOperator.Add;
}

class Sub(VariableExpr regdst, ExprBase value) : BinaryOp(regdst, value)
{
    protected override MetaOperator MetaInfo => MetaOperator.Sub;

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    if (RegDst is VariableExpr dest)
    //    {
    //        if (Value is InstantExpr val)
    //        {
    //            assembler.Add(Assembly.Instructions.AsmMov.Create(Assembly.Assembler.IReg, -val.Val));
    //            assembler.Add(Assembly.Instructions.AsmAdd.Create(dest.Reg, new VariableExpr(Assembly.Assembler.IReg)));
    //        }
    //        else if (Value is VariableExpr)
    //        {
    //            assembler.Add(Assembly.Instructions.AsmMov.Create(Assembly.Assembler.IReg, Value));
    //            assembler.Add(Assembly.Instructions.AsmNegative.Create(Assembly.Assembler.IReg));
    //            assembler.Add(Assembly.Instructions.AsmAdd.Create(dest.Reg, new VariableExpr(Assembly.Assembler.IReg)));
    //        }else
    //            throw new Assembly.AssembleException(ErrorMessage.NotSupported);
    //    }
    //    else
    //        throw new Assembly.AssembleException(ErrorMessage.NotSupported);
    //}
}

class Mul(VariableExpr regdst, ExprBase value) : BinaryOp(regdst, value)
{
    protected override MetaOperator MetaInfo => MetaOperator.Mul;
}

class Div(VariableExpr regdst, ExprBase value) : BinaryOp(regdst, value)
{
    protected override MetaOperator MetaInfo => MetaOperator.Div;
}

class RoundDiv(VariableExpr regdst, ExprBase value) : BinaryOp(regdst, value)
{
    protected override MetaOperator MetaInfo => MetaOperator.RoundDiv;

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    throw new Assembly.AssembleException(ErrorMessage.NotSupported);
    //}
}

class Mod(VariableExpr regdst, ExprBase value) : BinaryOp(regdst, value)
{
    protected override MetaOperator MetaInfo => MetaOperator.Mod;
}

class And(VariableExpr regdst, ExprBase value) : BinaryOp(regdst, value)
{
    protected override MetaOperator MetaInfo => MetaOperator.And;
}

class Or(VariableExpr regdst, ExprBase value) : BinaryOp(regdst, value)
{
    protected override MetaOperator MetaInfo => MetaOperator.Or;
}

class Xor(VariableExpr regdst, ExprBase value) : BinaryOp(regdst, value)
{
    protected override MetaOperator MetaInfo => MetaOperator.Xor;
}

class ShiftLeft(VariableExpr regdst, ExprBase value) : BinaryOp(regdst, value)
{
    protected override MetaOperator MetaInfo => MetaOperator.LShift;
}

class ShiftRight(VariableExpr regdst, ExprBase value) : BinaryOp(regdst, value)
{
    protected override MetaOperator MetaInfo => MetaOperator.RShift;
}
#endregion

#region unaryOp操作
class MetaU
{
    public static readonly List<MetaU> All = new();
    public readonly Type StatementType;
    public readonly Type InstructionType;
    public readonly string KeyWord;
    public readonly Func<int, int> Function;

    public MetaU(Type statementType, Type instructionType, string keyword, Func<int, int> function)
    {
        StatementType = statementType;
        InstructionType = instructionType;
        KeyWord = keyword;
        Function = function;
        All.Add(this);
    }

    public static readonly MetaU Not = new(typeof(Not), typeof(Assembly.Instructions.AsmNot), "~", a => ~a);
    public static readonly MetaU Negative = new(typeof(Negative), typeof(Assembly.Instructions.AsmNegative), "-", a => -a);
}

abstract class UnaryOp(VariableExpr regdst, VariableExpr regsrc) : Statement
{
    protected abstract MetaU MetaInfo { get; }
    public readonly VariableExpr RegDst = regdst;
    public readonly VariableExpr RegSrc = regsrc;

    protected override string _GetString()
    {
        return $"{RegDst.GetCodeText()} = {MetaInfo.KeyWord}{RegSrc.GetCodeText()}";
    }

    public override void Exec(Processor processor)
    {
        processor.Register[RegDst] = MetaInfo.Function(processor.Register[RegSrc]);
    }

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    if (RegDst is VariableExpr val)
    //    {
    //        if(val.Reg == 0)throw new Assembly.AssembleException(ErrorMessage.NotSupported);
    //        assembler.Add(Assembly.Instructions.AsmMov.Create(val.Reg, RegSrc));
    //        assembler.Add(Assembly.Instruction.CreateInstance(MetaInfo.InstructionType, val.Reg));
    //    }
    //}
}

class Not(VariableExpr regdst, VariableExpr regsrc) : UnaryOp(regdst, regsrc)
{
    protected override MetaU MetaInfo => MetaU.Not;
}

class Negative(VariableExpr regdst, VariableExpr regsrc) : UnaryOp(regdst, regsrc)
{
    protected override MetaU MetaInfo => MetaU.Negative;
}
#endregion