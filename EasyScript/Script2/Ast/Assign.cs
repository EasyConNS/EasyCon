using VBF.Compilers.Scanners;

namespace ECP.Ast;

public class Assign : Expression
{
    public Assign(LexemeValue dest, Expression expr)
    {
        DestVar = new Variable(dest);
        AssignExpr = expr;
    }

    public Variable DestVar { get; init; }
    public Expression AssignExpr { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitAssign(this);
    }
}