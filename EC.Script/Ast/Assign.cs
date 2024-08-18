using VBF.Compilers.Scanners;

namespace ECP.Ast;

public class Assign : Statement
{
    public Assign(Expression sufexpr, Expression resexpr)
    {
        SufExpr = sufexpr;
        AssignExpr = resexpr;
    }

    public Expression SufExpr { get; init; }
    public Expression AssignExpr { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitAssign(this);
    }
}