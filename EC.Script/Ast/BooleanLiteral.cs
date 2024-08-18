using VBF.Compilers;
using VBF.Compilers.Scanners;

namespace ECP.Ast;
public class BooleanLiteral : Expression
{
    private SourceSpan m_literalSpan;

    public BooleanLiteral(LexemeValue literal)
    {
        Value = literal.Content == null ? false : Boolean.Parse(literal.Content);
        m_literalSpan = literal.Span;
    }

    public bool Value { get; private set; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitBooleanLiteral(this);
    }
}