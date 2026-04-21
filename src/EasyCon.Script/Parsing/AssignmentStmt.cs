namespace EasyCon.Script.Syntax;

class AssignmentStmt(Token syntax, VariableExpr regdst, Token assignmentToken, ExprBase value) : Statement(syntax)
{
    public readonly VariableExpr DestVariable = regdst;
    public readonly Token AssignmentToken = assignmentToken;
    public readonly ExprBase Expression = value;

    protected override string _GetString()
    {
        return $"{DestVariable.GetCodeText()} {AssignmentToken.Value} {Expression.GetCodeText()}";
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