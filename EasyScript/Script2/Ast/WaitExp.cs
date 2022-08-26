namespace ECP.Ast;

public class WaitExp : Statement
{
    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitWaitExp(this);
    }

    public override void Show()
    {
        Console.WriteLine(this);
    }
}