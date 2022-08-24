namespace ECP.Ast;

public class IfElse : Statement
{
    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitIfElse(this);
    }
}