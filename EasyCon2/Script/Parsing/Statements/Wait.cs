namespace EasyCon2.Script.Parsing.Statements
{
    class Wait : Statement
    {
        protected readonly ValBase Duration;
        protected bool _omitted;

        public Wait(ValBase duration, bool omitted = false)
        {
            Duration = duration;
            _omitted = omitted;
        }

        protected override string _GetString(Formatter formatter)
        {
            return _omitted ? $"{Duration.GetCodeText(formatter)}" : $"WAIT {Duration.GetCodeText(formatter)}";
        }

        public override void Exec(Processor processor)
        {
            Thread.Sleep(Duration.Get(processor));
        }

        public override void Assemble(Assembly.Assembler assembler)
        {
            if (Duration is ValReg)
            {
                assembler.Add(Assembly.Instructions.AsmStoreOp.Create((Duration as ValReg).Index));
                assembler.Add(Assembly.Instructions.AsmWait.Create(0));
            }
            else if (Duration is ValInstant)
                assembler.Add(Assembly.Instructions.AsmWait.Create((Duration as ValInstant).Val));
            else
                throw new Assembly.AssembleException(ErrorMessage.NotSupported);
        }
    }
}
