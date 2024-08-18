using VBF.Compilers.Scanners;

namespace ECP.Ast;

public class ConstDefine : Statement
{
    public ConstDefine(LexemeValue name, LexemeValue value)
    {
        ConstName = name.Content;
        ConstValue = new Number(value);
    }

    public string ConstName { get; init; }
    public Number ConstValue { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitConstDefine(this);
    }
}