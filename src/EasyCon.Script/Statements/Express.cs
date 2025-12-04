using EasyScript.Parsing;

namespace EasyScript.Statements
{
    class Express(ValRegEx regdst, ValBase valueLeft, Meta? op, ValBase? valueRight) : Parsing.Statement
    {
        protected readonly ValRegEx RegDst = regdst;
        protected readonly ValBase ValueLeft = valueLeft;
        protected readonly Meta? OpMeta = op;
        protected readonly ValBase? ValueRight = valueRight;

        protected override string _GetString()
        {
            var expr = $"{RegDst.GetCodeText()} = {ValueLeft.GetCodeText()}";
            if (ValueRight != null)
            {
                expr += $" {OpMeta!.Operator} {ValueRight.GetCodeText()}";
            }
            return expr;
        }

        public override void Exec(Processor processor)
        {
            if (ValueRight != null)
            {
                processor.Register[RegDst] = OpMeta!.Function(ValueLeft.Get(processor), ValueRight!.Get(processor));
            }
            else
            {
                processor.Register[RegDst] = ValueLeft.Get(processor);
            }
        }

        public override void Assemble(Assembly.Assembler assembler)
        {
            if (RegDst is ValReg reg)
            {
                if (reg.Index >= Processor.OfflineMaxRegisterCount)
                    throw new Assembly.AssembleException(ErrorMessage.NotSupported);
                assembler.Add(Assembly.Instructions.AsmMov.Create(reg.Index, ValueLeft));
                if (OpMeta != null)
                {
                    if (OpMeta.Operator != "-")
                    {
                        assembler.Add(Assembly.Instruction.CreateInstance(OpMeta.InstructionType, reg.Index, ValueRight));
                    }
                    else
                    {
                        if (ValueRight is ValInstant)
                        {
                            assembler.Add(Assembly.Instructions.AsmMov.Create(Assembly.Assembler.IReg, -(ValueRight as ValInstant).Val));
                            assembler.Add(Assembly.Instructions.AsmAdd.Create(reg.Index, new ValReg(Assembly.Assembler.IReg)));
                        }
                        else if (ValueRight is ValReg)
                        {
                            assembler.Add(Assembly.Instructions.AsmMov.Create(Assembly.Assembler.IReg, ValueRight));
                            assembler.Add(Assembly.Instructions.AsmNegative.Create(Assembly.Assembler.IReg));
                            assembler.Add(Assembly.Instructions.AsmAdd.Create(reg.Index, new ValReg(Assembly.Assembler.IReg)));
                        }

                        else
                            throw new Assembly.AssembleException(ErrorMessage.NotSupported);
                    }
                }
            }
            else
            {
                throw new Assembly.AssembleException(ErrorMessage.NotSupported);
            }
        }
    }
}
