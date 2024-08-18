using VBF.Compilers;

namespace ECP.Ast;

public class Not : Expression
{
    public Not(Expression exp, SourceSpan opSpan)
    {
        Operand = exp;
        OpSpan = opSpan;
    }

    public Expression Operand { get; private set; }
    public SourceSpan OpSpan { get; private set; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitNot(this);
    }
}