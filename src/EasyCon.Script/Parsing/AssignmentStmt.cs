namespace EasyCon.Script.Parsing;

class ConstDeclStmt(string tag, string txt): Statement
{
    private readonly string Tag = tag;
    private readonly string Text = txt;

    protected override string _GetString() => $"{Tag} = {Text}";
}

class AssignmentStmt(VariableExpr regdst, ExprBase value, MetaOperator? augop = null) : Statement
{
    public readonly VariableExpr DestVariable = regdst;
    public readonly ExprBase Expression = value;

    public readonly MetaOperator? AugOp = augop;

    protected override string _GetString()
    {
        var op = "=";
        if( AugOp != null )
            op = AugOp!.Operator+"=";
        return $"{DestVariable.GetCodeText()} {op} {Expression.GetCodeText()}";
    }

    //public override void Exec(Processor processor)
    //{
    //    DestVariable.Set(processor, BinaryExpression.Rewrite(Expression).Get(processor));
    //}

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

    //public override void Exec(Processor processor)
    //{
    //    processor.Register[RegDst] = MetaInfo.Function(processor.Register[RegSrc]);
    //}

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