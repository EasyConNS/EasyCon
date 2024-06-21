using VBF.Compilers.Scanners;

namespace ECP.Ast;

public class BuildinState : Statement
{
    public BuildinState(string name, LexemeValue args)
    {
        BuildinFunc = name;
        Args = args;
    }

    public string BuildinFunc { get; init; }
    public LexemeValue Args { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitBuildinState(this);
    }
}