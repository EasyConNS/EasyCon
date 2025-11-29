namespace EasyScript.Parsing
{
    abstract class Content
    {
        public abstract string GetPrintString(Processor processor);
        public abstract string GetCodeString(Formatter formatter);
    }

    class TextContent : Content
    {
        public readonly string Text;
        public readonly string CodeText;

        public TextContent(string text, string codetext = null)
        {
            Text = text;
            CodeText = codetext ?? text;
        }

        public override string GetPrintString(Processor _)
        {
            return Text;
        }

        public override string GetCodeString(Formatter _)
        {
            return CodeText;
        }
    }

    class RegContent : Content
    {
        public readonly ValRegEx Reg;

        public RegContent(ValRegEx reg)
        {
            Reg = reg;
        }

        public override string GetPrintString(Processor processor)
        {
            return processor.Register[Reg].ToString();
        }

        public override string GetCodeString(Formatter _)
        {
            return Reg.GetCodeText();
        }
    }
}
