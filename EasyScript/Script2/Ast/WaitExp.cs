namespace ECP.Ast;

public class WaitExp : Statement
{
    public WaitExp(Number duration)
    {
        Duration = duration;
    }

    public Number Duration { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitWaitExp(this);
    }
}