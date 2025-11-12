namespace EasyScript.Parsing.Statements
{
    class Expr : Statement
    {
        protected readonly ValRegEx RegDst;
        protected readonly ValBase ValueLeft;
        protected readonly Meta? OpMeta;
        protected readonly ValBase? ValueRight;

        public Expr(ValRegEx regdst, ValBase valueLeft, Meta? op, ValBase? valueRight)
        {
            RegDst = regdst;
            ValueLeft = valueLeft;
            OpMeta = op;
            ValueRight = valueRight;
        }

        protected override string _GetString(Formatter formatter)
        {
            var expr = $"{RegDst.GetCodeText(formatter)} = {ValueLeft.GetCodeText(formatter)}";
            if (ValueRight != null)
            {
                expr += $" {OpMeta!.Operator} {ValueRight.GetCodeText(formatter)}";
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
                    throw new Assembly.AssembleException(ErrorMessage.RegisterCountNotSupported);
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
