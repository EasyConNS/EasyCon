namespace EasyScript.Parsing.Statements
{
    class Alert : Statement
    {
        protected readonly Content[] Contents;

        public Alert(Content[] contents)
        {
            Contents = contents;
        }

        protected override string _GetString(Formatter formatter)
        {
            return Contents.Length == 0 ? "ALERT" : $"ALERT {string.Join(" & ", Contents.Select(u => u.GetCodeString(formatter)))}";
        }

        public override void Exec(Processor processor)
        {
            processor.Output.Alert(string.Join("", Contents.Select(u => u.GetPrintString(processor))));
        }

        public override void Assemble(Assembly.Assembler assembler)
        { }
    }
}
