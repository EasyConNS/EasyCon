using VBF.Compilers.Scanners;

namespace ECP.Ast;

public class Wait : Statement
{
    public Wait(Number duration)
    {
        Duration = duration;
    }

    public Wait(LexemeValue durVal)
    {
        Duration = new Number(durVal);
    }

    public Number Duration { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitWait(this);
    }
}