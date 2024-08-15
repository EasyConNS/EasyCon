namespace ECP.Ast;

public abstract class Expression : Statement
{
}

public class SimpleExpr : Statement
{
    public SimpleExpr(string txt)
    {
        Content = txt;
    }

    public string Content { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        throw new NotImplementedException();
    }
}