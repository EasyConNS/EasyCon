namespace ECP.Ast;

public class ForStatement : Statement
{
    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitForState(this);
    }

    public override void Show()
    {
        Console.WriteLine(this);
    }
}