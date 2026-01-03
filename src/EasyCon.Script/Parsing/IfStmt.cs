using System.Collections.Immutable;

namespace EasyCon.Script.Parsing;

[Obsolete]
abstract class BranchOp : Statement
{
    public BranchOp? If;
    public BranchOp? Else;
    public EndIf EndIf;
    public bool Passthrough = true;
}

internal sealed class IfBlock(IfStmt condition, ImmutableArray<Statement> statements, EndIf endif) : Statement
{
    public readonly IfStmt Condition = condition;
    public ImmutableArray<Statement> Statements = statements;
    public readonly EndIf End = endif;

    protected override string _GetString()
    {
        throw new NotImplementedException();
    }
}

class IfStmt(CmpExpression conds) : BranchOp
{
    public readonly CmpExpression Condition = conds;

    //public override void Exec(Processor processor)
    //{
    //    Passthrough = true;
    //    if (Condition.Get(processor) == 1)
    //    {
    //        // do nothing
    //        Passthrough = false;
    //    }
    //    else
    //    {
    //        // jump
    //        if (Else != null)
    //            processor.PC = Else.Address;
    //        else
    //            processor.PC = EndIf.Address + 1;
    //    }
    //}

    protected override string _GetString()
    {
        return $"IF {Condition.GetCodeText()}";
    }

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    throw new Assembly.AssembleException(ErrorMessage.NotSupported);
    //    // if (Left is not ValReg left)
    //    //     throw new Assembly.AssembleException(ErrorMessage.NotSupported);
    //    // if (Right is ValInstant)
    //    // {
    //    //     assembler.Add(Assembly.Instructions.AsmMov.Create(Assembly.Assembler.IReg, Right));
    //    //     assembler.Add(Operater.Assemble(left.Reg, Assembly.Assembler.IReg));
    //    // }
    //    // else
    //    // {
    //    //     assembler.Add(Operater.Assemble(left.Reg, (Right as ValReg).Reg));
    //    // }
    //    // assembler.Add(Assembly.Instructions.AsmBranchFalse.Create());
    //    // assembler.IfMapping[this] = assembler.Last() as Assembly.Instructions.AsmBranchFalse;
    //}
}

class ElseIf(CmpExpression conds) : IfStmt(conds)
{
    //public override void Exec(Processor processor)
    //{
    //    if (!If.Passthrough)
    //    {
    //        processor.PC = If.EndIf.Address + 1;
    //    }
    //    else
    //    {
    //        Passthrough = true;
    //        if (Condition.Get(processor) == 1)
    //        {
    //            // do nothing
    //            Passthrough = false;
    //        }
    //        else
    //        {
    //            // jump
    //            if (Else != null)
    //                processor.PC = Else.Address;
    //            else
    //                processor.PC = EndIf.Address + 1;
    //        }
    //    }
    //}

    protected override string _GetString()
    {
        return $"ELIF {Condition.GetCodeText()}";
    }

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    throw new Assembly.AssembleException(ErrorMessage.NotSupported);
    //    // assembler.Add(Assembly.Instructions.AsmBranch.Create());
    //    // assembler.ElseMapping[this] = assembler.Last() as Assembly.Instructions.AsmBranch;
    //    // assembler.Add(Assembly.Instructions.AsmEmpty.Create());
    //    // assembler.IfMapping[If].Target = assembler.Last();

    //    // if (Left is not ValReg left)
    //    //     throw new Assembly.AssembleException("外部变量仅限联机模式使用");
    //    // if (Right is ValInstant)
    //    // {
    //    //     assembler.Add(Assembly.Instructions.AsmMov.Create(Assembly.Assembler.IReg, Right));
    //    //     assembler.Add(Operater.Assemble(left.Reg, Assembly.Assembler.IReg));
    //    // }
    //    // else
    //    // {
    //    //     assembler.Add(Operater.Assemble(left.Reg, (Right as ValReg).Reg));
    //    // }
    //    // assembler.Add(Assembly.Instructions.AsmBranchFalse.Create());
    //    // assembler.IfMapping[this] = assembler.Last() as Assembly.Instructions.AsmBranchFalse;
    //}
}

class Else : BranchOp
{
    //public override void Exec(Processor processor)
    //{
    //    // end of if-block
    //    if (!If.Passthrough)
    //        processor.PC = If.EndIf.Address + 1;
    //}

    protected override string _GetString() => "ELSE";

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    assembler.Add(Assembly.Instructions.AsmBranch.Create());
    //    assembler.ElseMapping[this] = assembler.Last() as Assembly.Instructions.AsmBranch;
    //    assembler.Add(Assembly.Instructions.AsmEmpty.Create());
    //    assembler.IfMapping[If].Target = assembler.Last();
    //}
}

class EndIf : BranchOp
{
    protected override string _GetString() => "ENDIF";

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    assembler.Add(Assembly.Instructions.AsmEmpty.Create());
    //    if (If.Else == null)
    //        assembler.IfMapping[If].Target = assembler.Last();
    //    else
    //    {
    //        assembler.ElseMapping[If.Else].Target = assembler.Last();
    //        var @elif = If;
    //        while (@elif is ElseIf)
    //        {
    //            assembler.ElseMapping[@elif].Target = assembler.Last();
    //            @elif = @elif.If;
    //        }
    //    }
    //}
}
