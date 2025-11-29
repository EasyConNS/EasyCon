using EasyScript.Parsing;

namespace EasyScript.Statements;

class TimeStamp : Parsing.Statement
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

    protected override string _GetString(Formatter _)
    {
        return $"TIME {RegDst.GetCodeText()}";
    }
}
