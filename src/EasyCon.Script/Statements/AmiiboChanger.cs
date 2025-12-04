using EasyScript.Parsing;

namespace EasyScript.Statements
{
    class AmiiboChanger : Statement
    {
        protected readonly ValBase Index;

        public AmiiboChanger(ValBase value)
        {
            Index = value;
        }

        protected override string _GetString()
        {
            return $"AMIIBO {Index.GetCodeText()}";
        }

        public override void Exec(Processor processor)
        {
            var index = Index.Get(processor);
            if(index > 9)
            {
                // value must between 0~9
                return;
            }
            processor.GamePad.ChangeAmiibo((uint)index);
        }

        public override void Assemble(Assembly.Assembler _)
        {
            throw new Assembly.AssembleException(ErrorMessage.NotSupported);
        }
    }
}
