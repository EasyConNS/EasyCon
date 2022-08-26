namespace ECP.Ast;

public class MovStatement : Expression
{
    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitMovStatement(this);
    }

    public override void Show()
    {
        Console.WriteLine(this);
    }
}