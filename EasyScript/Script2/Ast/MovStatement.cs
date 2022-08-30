using VBF.Compilers.Scanners;

namespace ECP.Ast;

public class MovStatement : Expression
{
    public MovStatement(LexemeValue dest, Expression expr)
    {
        DestVar = new Number(dest);
        AssignExpr = expr;
    }

    public Number DestVar { get; init; }
    public Expression AssignExpr { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitMovStatement(this);
    }
}