namespace ECP.Ast;

public class FuncState : Statement
{
    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitFuncState(this);
    }
}