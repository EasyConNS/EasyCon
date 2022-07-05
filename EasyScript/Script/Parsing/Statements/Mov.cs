namespace EasyScript.Parsing.Statements
{
    class Mov : Statement
    {
        protected readonly ValRegEx RegDst;
        protected readonly ValBase Value;

        public Mov(ValRegEx regdst, ValBase value)
        {
            RegDst = regdst;
            Value = value;
        }

        protected override string _GetString(Formatter formatter)
        {
            return $"{RegDst.GetCodeText(formatter)} = {Value.GetCodeText(formatter)}";
        }

        public override void Exec(Processor processor)
        {
            processor.Register[RegDst] = Value.Get(processor);
        }

        public override void Assemble(Assembly.Assembler assembler)
        {
            if (RegDst is ValReg reg)
            {
                if (reg.Index >= Processor.OfflineMaxRegisterCount)
                    throw new Assembly.AssembleException(ErrorMessage.RegisterCountNotSupported);
                assembler.Add(Assembly.Instructions.AsmMov.Create(reg.Index, Value));
            }else
            {
                throw new Assembly.AssembleException(ErrorMessage.NotSupported);
            }
        }
    }
}
