using VBF.Compilers.Scanners;

namespace ECP.Ast;

public class Number : Expression
{
    public Number(LexemeValue name)
    {
        VariableRef = name;
    }

    public LexemeValue VariableRef { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitNumber(this);
    }
}