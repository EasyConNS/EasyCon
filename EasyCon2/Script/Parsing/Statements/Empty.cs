namespace EasyCon2.Script.Parsing.Statements
{
    class Empty : Statement
    {
        private readonly string Text;

        public Empty(string text = "")
        {
            Text = text;
        }

        protected override string _GetString(Formatter formatter)
        {
            return Text;
        }

        public override void Exec(Processor processor)
        { }

        public override void Assemble(Assembly.Assembler assembler)
        { }
    }
}
