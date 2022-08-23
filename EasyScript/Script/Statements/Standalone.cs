using EasyScript.Assembly;

namespace EasyScript.Parsing.Statements
{
    class PushAll : Statement
    {
        protected override string _GetString(Formatter formatter)
        {
            return $"PUSHALL";
        }

        public override void Exec(Processor processor)
        {
            for (uint i = 1; i < Processor.RegisterCount; i++)
                processor.Stack.Push(processor.Register[i]);
        }

        public override void Assemble(Assembler assembler)
        {
            throw new AssembleException(ErrorMessage.NotSupported);
        }
    }

    class PopAll : Statement
    {
        protected override string _GetString(Formatter formatter)
        {
            return $"POPALL";
        }

        public override void Exec(Processor processor)
        {
            for (uint i = Processor.RegisterCount - 1; i >= 1; i--)
                processor.Register[i] = processor.Stack.Pop();
        }

        public override void Assemble(Assembler assembler)
        {
            throw new AssembleException(ErrorMessage.NotSupported);
        }
    }
}
