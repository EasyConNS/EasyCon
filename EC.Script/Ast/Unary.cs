using VBF.Compilers.Scanners;

namespace ECP.Ast;

public class Unary : Expression
{
    public Unary(LexemeValue op)
    {
        OpLexeme = op;
    }

    public LexemeValue OpLexeme { get; private set; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitUnary(this);
    }
}