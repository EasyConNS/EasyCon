namespace ECP.Ast;

public class BuildinState : Statement
{
    public BuildinState(string ident)
    {
        BuildinFunc = ident;
    }

    public string BuildinFunc { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitBuildinState(this);
    }
}