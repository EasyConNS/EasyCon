using VBF.Compilers.Scanners;

namespace ECP.Ast;

public class Assign : Expression
{
    public Assign(LexemeValue var, Expression expr)
    {
        Variable = new VariableRef(var);
        AssignExpr = expr;
    }

    public VariableRef Variable { get; init; }
    public Expression AssignExpr { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitAssign(this);
    }
}