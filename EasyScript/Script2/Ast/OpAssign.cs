using VBF.Compilers.Scanners;

namespace ECP.Ast;

public class OpAssign : Expression
{
    public OpAssign(LexemeValue op, LexemeValue n1, Expression expr)
    {
        Operator = op;
        DestVar = new Variable(n1);
        AssignExpr = expr;
    }

    public LexemeValue Operator { get; init; }
    public Variable DestVar { get; init; }
    public Expression AssignExpr { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitOpAssign(this);
    }
}
