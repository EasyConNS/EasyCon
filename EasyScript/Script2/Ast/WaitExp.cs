namespace ECP.Ast;

public class WaitExp : Statement
{
    public WaitExp(Number number, uint duration = 50)
    {
        m_number = number;
        Duration = duration;
    }

    public Number m_number { get; init; }
    public uint Duration { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitWaitExp(this);
    }
}