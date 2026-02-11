using System.Collections.Immutable;

namespace EasyCon.Script.Parsing;

internal sealed class IfBlock(IfStmt condition, ImmutableArray<Statement> statements, EndBlockStmt endif) : Statement
{
    public readonly IfStmt Condition = condition;
    public ImmutableArray<Statement> Statements = statements;
    public readonly EndBlockStmt End = endif;

    protected override string _GetString()
    {
        throw new NotImplementedException();
    }
}

class IfStmt(ExprBase conds) : Statement
{
    public readonly ExprBase Condition = conds;

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

class ElseIf(ExprBase conds) : IfStmt(conds)
{
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

class Else : Statement
{
    protected override string _GetString() => "ELSE";

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    assembler.Add(Assembly.Instructions.AsmBranch.Create());
    //    assembler.ElseMapping[this] = assembler.Last() as Assembly.Instructions.AsmBranch;
    //    assembler.Add(Assembly.Instructions.AsmEmpty.Create());
    //    assembler.IfMapping[If].Target = assembler.Last();
    //}
}

class EndIf : EndBlockStmt
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
