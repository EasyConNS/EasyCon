using EasyScript.Parsing;

namespace EasyScript.Statements
{
    class Wait(ValBase duration, bool omitted = false) : Statement
    {
        protected readonly ValBase Duration = duration;
        protected bool _omitted = omitted;

        protected override string _GetString(Formatter _)
        {
            if (omitted) return $"{Duration.GetCodeText()}";
            return $"WAIT {Duration.GetCodeText()}";
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
