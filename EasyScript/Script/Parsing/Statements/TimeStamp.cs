namespace EasyScript.Parsing.Statements;

class TimeStamp : Statement
{
    private readonly ValRegEx RegDst;

    public TimeStamp(ValRegEx val)
    {
        RegDst = val;
    }

    public override void Exec(Processor processor)
    {
        processor.Register[RegDst] = processor.et.CurrTimestamp;
    }

    public override void Assemble(Assembly.Assembler assembler)
    {
        throw new Assembly.AssembleException(ErrorMessage.NotSupported);
    }

    protected override string _GetString(Formatter formatter)
    {
        return $"TIME {RegDst.GetCodeText(formatter)}";
    }
}
