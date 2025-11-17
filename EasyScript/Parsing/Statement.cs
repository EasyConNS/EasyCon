namespace EasyScript.Parsing;

abstract class Statement
{
    public int Address = -1;
    public string Indent { get; set; }
    public string Comment { get; set; }
    public virtual int IndentThis => 0;
    public virtual int IndentNext => 0;

    public abstract void Exec(Processor processor);

    public abstract void Assemble(Assembly.Assembler assembler);

    public string GetString(Formatter formatter)
    {
        return $"{Indent}{_GetString(formatter)}{Comment}";
    }

    protected abstract string _GetString(Formatter formatter);

    protected static class ErrorMessage
    {
        public const string NotSupported = "脚本中存在仅支持联机模式的语句";
        public const string RegisterCountNotSupported = "联机寄存器不能烧录为固件模式";
        public const string NotImplemented = "类型未定义，这可能是一个bug，请汇报给作者";
    }
}
