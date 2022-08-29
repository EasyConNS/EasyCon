namespace ECP.Ast;

public abstract class Expression : Statement
{
}

public class Empty : Statement
{
    public Empty(string txt)
    {
        Content = txt;
    }

    public string Content { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        throw new NotImplementedException();
    }
}