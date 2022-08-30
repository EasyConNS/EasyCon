using VBF.Compilers.Scanners;

namespace ECP.Ast;

public class Binary : Expression
{
    public Binary(string op, LexemeValue name, Number num)
    {
        Operator = op;
        m_number = new Number(name);
        r_number = num;
    }

    public Binary(string op, Number n1, Number n2)
    {
        Operator = op;
        m_number = n1;
        r_number = n2;
    }

    public string Operator { get; init; }
    public Number m_number { get; init; }
    public Number r_number { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitBinary(this);
    }
}
