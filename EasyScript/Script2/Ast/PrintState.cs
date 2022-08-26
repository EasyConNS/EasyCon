namespace ECP.Ast;

public class PrintState : Statement
{
    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitPrintState(this);
    }

    public override void Show()
    {
        Console.WriteLine(this);
    }
}