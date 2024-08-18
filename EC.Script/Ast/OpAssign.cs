using VBF.Compilers.Scanners;

namespace ECP.Ast;

public class OpAssign : Expression
{
    public OpAssign(LexemeValue op, LexemeValue name, Expression expr)
    {
        Variable = new Variable(name);
        Operator = op;
        AssignExpr = expr;
    }

    public LexemeValue Operator { get; init; }
    public Variable Variable { get; init; }
    public Expression AssignExpr { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitOpAssign(this);
    }
}
