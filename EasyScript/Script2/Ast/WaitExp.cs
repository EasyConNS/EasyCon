using VBF.Compilers.Scanners;

namespace ECP.Ast;

public class WaitExp : Statement
{
    public WaitExp(Number duration)
    {
        Duration = duration;
    }

    public WaitExp(LexemeValue durVal)
    {
        Duration = new Number(durVal);
    }

    public Number Duration { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitWait(this);
    }
}