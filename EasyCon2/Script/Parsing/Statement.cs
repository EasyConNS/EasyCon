namespace EasyCon2.Script.Parsing
{
    abstract class Statement
    {
        public int Address = -1;
        public string Indent;
        public string Comment;
        public virtual int IndentThis => 0;
        public virtual int IndentNext => 0;

        public abstract void Exec(Processor processor);

        public abstract void Assemble(Assembly.Assembler assembler);

        public string GetString(Formats.Formatter formatter)
        {
            return $"{Indent}{_GetString(formatter)}{Comment}";
        }

        protected abstract string _GetString(Formats.Formatter formatter);

        protected static class ErrorMessage
        {
            public const string NotSupported = "该语句目前仅支持联机模式";
            public const string NotImplemented = "类型未定义，这可能是一个bug，请汇报给作者";
        }
    }
}
