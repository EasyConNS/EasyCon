using VBF.Compilers.Scanners;

namespace ECP.Ast;

public class Number : Expression
{
    public Number(LexemeValue literal)
    {
        Literal = literal;
    }

    public LexemeValue Literal { get; private set; }
    public int Value { get; set; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitNumber(this);
    }
}
