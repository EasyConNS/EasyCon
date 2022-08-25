namespace ECP.Ast;

public class Binary : Expression
{
    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitBinary(this);
    }
}
