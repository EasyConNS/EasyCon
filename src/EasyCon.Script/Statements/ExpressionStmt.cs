using EasyScript.Parsing;

namespace EasyScript.Statements;

class ExpressionStmt(ValReg regdst, ValBase value) : Statement
{
    protected readonly ValReg RegDst = regdst;
    protected readonly ValBase Value = value;

    protected override string _GetString()
    {
        return $"{RegDst.GetCodeText()} = {Value.GetCodeText()}";
    }

    public override void Exec(Processor processor)
    {
        RegDst.Set(processor, BinExpr.Rewrite(Value).Get(processor));
    }

    public override void Assemble(Assembly.Assembler assembler)
    {
        if (RegDst is ValReg reg)
        {
            // assembler.Add(Assembly.Instructions.AsmMov.Create(reg.Index, ValueLeft));
            // if (OpMeta != null)
            // {
            //     if (OpMeta.Operator != "-")
            //     {
            //         assembler.Add(Assembly.Instruction.CreateInstance(OpMeta.InstructionType, reg.Index, ValueRight));
            //     }
            //     else
            //     {
            //         if (ValueRight is ValInstant)
            //         {
            //             assembler.Add(Assembly.Instructions.AsmMov.Create(Assembly.Assembler.IReg, -(ValueRight as ValInstant).Val));
            //             assembler.Add(Assembly.Instructions.AsmAdd.Create(reg.Index, new ValReg(Assembly.Assembler.IReg)));
            //         }
            //         else if (ValueRight is ValReg)
            //         {
            //             assembler.Add(Assembly.Instructions.AsmMov.Create(Assembly.Assembler.IReg, ValueRight));
            //             assembler.Add(Assembly.Instructions.AsmNegative.Create(Assembly.Assembler.IReg));
            //             assembler.Add(Assembly.Instructions.AsmAdd.Create(reg.Index, new ValReg(Assembly.Assembler.IReg)));
            //         }

            //         else
            //             throw new Assembly.AssembleException(ErrorMessage.NotSupported);
            //     }
            // }
            throw new Assembly.AssembleException(ErrorMessage.NotSupported);
        }
        else
        {
            throw new Assembly.AssembleException(ErrorMessage.NotSupported);
        }
    }
}
