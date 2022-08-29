using VBF.Compilers.Scanners;

namespace ECP.Ast;

public class Binary : Expression
{
    public Binary(LexemeValue name, Number num)
    {
        m_number = new Number(name);
        r_number = num;
    }

    public Binary(Number n1, Number n2)
    {
        m_number = n1;
        r_number = n2;
    }

    public Number m_number { get; init; }
    public Number r_number { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitBinary(this);
    }
}
