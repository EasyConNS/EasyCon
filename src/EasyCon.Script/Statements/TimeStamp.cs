using EasyScript.Parsing;

namespace EasyScript.Statements;

class TimeStamp : Statement
{
    private readonly ValRegEx RegDst;

    public TimeStamp(ValRegEx val)
    {
        RegDst = val;
    }

    public override void Exec(Processor processor)
    {
        processor.CurTime(RegDst);
    }

    public override void Assemble(Assembly.Assembler assembler)
    {
        throw new Assembly.AssembleException(ErrorMessage.NotSupported);
    }

    protected override string _GetString()
    {
        return $"TIME {RegDst.GetCodeText()}";
    }
}
