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
