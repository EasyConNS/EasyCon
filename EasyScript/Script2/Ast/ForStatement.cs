using VBF.Compilers.Scanners;

namespace ECP.Ast;

public class ForStatement : Expression
{
    public ForStatement(Number loopCount)
    {
        Desp = $"{loopCount.VariableRef.Content}";
    }

    public ForStatement(LexemeValue name, Number start, Number end)
    {
        Desp = $"{name.Content} = {start.VariableRef.Content} to {end.VariableRef.Content}";
    }

    public string Desp { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitForState(this);
    }
}