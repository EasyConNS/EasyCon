namespace EasyScript.Parsing.Statements
{
    abstract class BranchOp : Statement
    {
        public BranchOp? If;
        public BranchOp? Else;
        public EndIf EndIf;
        public bool Passthrough = true;
    }

    class If : BranchOp
    {
        public override int IndentNext => 1;
        public readonly CompareOperator Operater;
        public readonly ValVar Left;
        public readonly ValBase Right;

        public If(CompareOperator op, ValVar left, ValBase right)
        {
            Operater = op;
            Left = left;
            Right = right;
        }

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

        protected override string _GetString(Formatter formatter)
        {
            var op = Operater.Operator == "=" ? "==" : Operater.Operator;
            return $"IF {Left.GetCodeText(formatter)} {op} {Right.GetCodeText(formatter)}";
        }

        public override void Assemble(Assembly.Assembler assembler)
        {
            if (Left is not ValReg left)
                throw new Assembly.AssembleException(ErrorMessage.NotSupported);
            if (Right is ValInstant)
            {
                assembler.Add(Assembly.Instructions.AsmMov.Create(Assembly.Assembler.IReg, Right));
                assembler.Add(Operater.Assemble(left.Index, Assembly.Assembler.IReg));
            }
            else
            {
                assembler.Add(Operater.Assemble(left.Index, (Right as ValReg).Index));
            }
            assembler.Add(Assembly.Instructions.AsmBranchFalse.Create());
            assembler.IfMapping[this] = assembler.Last() as Assembly.Instructions.AsmBranchFalse;
        }
    }

    class ElseIf : If
    {
        public override int IndentThis => -1;

        public ElseIf(CompareOperator op, ValVar left, ValBase right) : base(op, left, right)
        { }

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

        protected override string _GetString(Formatter formatter)
        {
            var op = Operater.Operator == "=" ? "==" : Operater.Operator;
            return $"ELIF {Left.GetCodeText(formatter)} {op} {Right.GetCodeText(formatter)}";
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
                assembler.Add(Operater.Assemble(left.Index, Assembly.Assembler.IReg));
            }
            else
            {
                assembler.Add(Operater.Assemble(left.Index, (Right as ValReg).Index));
            }
            assembler.Add(Assembly.Instructions.AsmBranchFalse.Create());
            assembler.IfMapping[this] = assembler.Last() as Assembly.Instructions.AsmBranchFalse;
        }
    }

    class Else : BranchOp
    {
        public override int IndentThis => -1;
        public override int IndentNext => 1;

        public override void Exec(Processor processor)
        {
            // end of if-block
            if(!If.Passthrough)
                processor.PC = If.EndIf.Address + 1;
        }

        protected override string _GetString(Formatter formatter)
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
        public override int IndentThis => -1;

        public override void Exec(Processor processor)
        { }

        protected override string _GetString(Formatter formatter)
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
}
