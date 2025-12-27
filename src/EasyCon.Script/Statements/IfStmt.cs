using EasyScript.Parsing;

namespace EasyScript.Statements;

abstract class BranchOp : Statement
{
    public BranchOp? If;
    public BranchOp? Else;
    public EndIf EndIf;
    public bool Passthrough = true;
}

class IfStmt(CompareOperator op, ValBase left, ValBase right) : BranchOp
{
    public readonly CompareOperator Operater = op;
    public readonly ValBase Left = left;
    public readonly ValBase Right = right;

    public override void Exec(Processor processor)
    {
        Passthrough = true;
        if (Operater.Compare(Left.Get(processor), Right.Get(processor)))
        {
            // do nothing
            Passthrough = false;
        }
        else
        {
            // jump
            if (Else != null)
                processor.PC = Else.Address;
            else
                processor.PC = EndIf.Address + 1;
        }
    }

    protected override string _GetString()
    {
        var op = Operater.Operator == "=" ? "==" : Operater.Operator;
        return $"IF {Left.GetCodeText()} {op} {Right.GetCodeText()}";
    }

    public override void Assemble(Assembly.Assembler assembler)
    {
        if (Left is not ValReg left)
            throw new Assembly.AssembleException(ErrorMessage.NotSupported);
        if (Right is ValInstant)
        {
            assembler.Add(Assembly.Instructions.AsmMov.Create(Assembly.Assembler.IReg, Right));
            assembler.Add(Operater.Assemble(left.Reg, Assembly.Assembler.IReg));
        }
        else
        {
            assembler.Add(Operater.Assemble(left.Reg, (Right as ValReg).Reg));
        }
        assembler.Add(Assembly.Instructions.AsmBranchFalse.Create());
        assembler.IfMapping[this] = assembler.Last() as Assembly.Instructions.AsmBranchFalse;
    }
}

class ElseIf(CompareOperator op, ValBase left, ValBase right) : IfStmt(op, left, right)
{
    public override void Exec(Processor processor)
    {
        if(!If.Passthrough)
        {
            processor.PC = If.EndIf.Address + 1;
        }
        else
        {
            Passthrough = true;
            if (Operater.Compare(Left.Get(processor), Right.Get(processor)))
            {
                // do nothing
                Passthrough = false;
            }
            else
            {
                // jump
                if (Else != null)
                    processor.PC = Else.Address;
                else
                    processor.PC = EndIf.Address + 1;
            }
        }   
    }

    protected override string _GetString()
    {
        var op = Operater.Operator == "=" ? "==" : Operater.Operator;
        return $"ELIF {Left.GetCodeText()} {op} {Right.GetCodeText()}";
    }

    public override void Assemble(Assembly.Assembler assembler)
    {
        // need test
        assembler.Add(Assembly.Instructions.AsmBranch.Create());
        assembler.ElseMapping[this] = assembler.Last() as Assembly.Instructions.AsmBranch;
        assembler.Add(Assembly.Instructions.AsmEmpty.Create());
        assembler.IfMapping[If].Target = assembler.Last();

        if (Left is not ValReg left)
            throw new Assembly.AssembleException("外部变量仅限联机模式使用");
        if (Right is ValInstant)
        {
            assembler.Add(Assembly.Instructions.AsmMov.Create(Assembly.Assembler.IReg, Right));
            assembler.Add(Operater.Assemble(left.Reg, Assembly.Assembler.IReg));
        }
        else
        {
            assembler.Add(Operater.Assemble(left.Reg, (Right as ValReg).Reg));
        }
        assembler.Add(Assembly.Instructions.AsmBranchFalse.Create());
        assembler.IfMapping[this] = assembler.Last() as Assembly.Instructions.AsmBranchFalse;
    }
}

class Else : BranchOp
{
    public override void Exec(Processor processor)
    {
        // end of if-block
        if(!If.Passthrough)
            processor.PC = If.EndIf.Address + 1;
    }

    protected override string _GetString()
    {
        return "ELSE";
    }

    public override void Assemble(Assembly.Assembler assembler)
    {
        assembler.Add(Assembly.Instructions.AsmBranch.Create());
        assembler.ElseMapping[this] = assembler.Last() as Assembly.Instructions.AsmBranch;
        assembler.Add(Assembly.Instructions.AsmEmpty.Create());
        assembler.IfMapping[If].Target = assembler.Last();
    }
}

class EndIf : BranchOp
{
    public override void Exec(Processor _)
    { }

    protected override string _GetString()
    {
        return "ENDIF";
    }

    public override void Assemble(Assembly.Assembler assembler)
    {
        assembler.Add(Assembly.Instructions.AsmEmpty.Create());
        if (If.Else == null)
            assembler.IfMapping[If].Target = assembler.Last();
        else
        {
            assembler.ElseMapping[If.Else].Target = assembler.Last();
            var @elif = If;
            while (@elif is ElseIf)
            {
                assembler.ElseMapping[@elif].Target = assembler.Last();
                @elif = @elif.If;
            }
        }
    }
}
