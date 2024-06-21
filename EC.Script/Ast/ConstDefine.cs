using VBF.Compilers.Scanners;

namespace ECP.Ast;

public class ConstDefine : Expression
{
    public ConstDefine(LexemeValue name, LexemeValue value)
    {
        ConstName = name;
        ConstValue = value;
    }

    public LexemeValue ConstName { get; init; }
    public LexemeValue ConstValue { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitConstDefine(this);
    }
}