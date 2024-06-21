using VBF.Compilers.Scanners;

namespace ECP.Ast;

public class Not : Expression
{
    public Not(Expression exp)
    {
        Operand = exp;
    }

    public Expression Operand { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitNot(this);
    }
}