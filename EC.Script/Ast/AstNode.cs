namespace ECP.Ast;

public abstract class AstNode
{
    public abstract T Accept<T>(IAstVisitor<T> visitor);
}

public abstract class Statement : AstNode
{
}
