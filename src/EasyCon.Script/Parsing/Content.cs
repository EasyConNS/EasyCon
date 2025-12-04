namespace EasyScript.Parsing
{
    abstract class Content
    {
        public abstract string GetPrintString(Processor processor);
        public abstract string GetCodeString();
    }

    class TextContent(string text, string codetext = null) : Content
    {
        public readonly string Text = text;
        public readonly string CodeText = codetext ?? text;

        public override string GetPrintString(Processor _)
        {
            return Text;
        }

        public override string GetCodeString()
        {
            return CodeText;
        }
    }

    class RegContent(ValRegEx reg) : Content
    {
        public readonly ValRegEx Reg = reg;

        public override string GetPrintString(Processor processor)
        {
            return processor.Register[Reg].ToString();
        }

        public override string GetCodeString()
        {
            return Reg.GetCodeText();
        }
    }
}
