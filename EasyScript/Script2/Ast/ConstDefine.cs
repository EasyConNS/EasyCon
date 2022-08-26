namespace ECP.Ast;

public class ConstDefine : Expression
{
     public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitConstDefine(this);
    }
    public override void Show()
    {
        Console.WriteLine(this);
    }
}