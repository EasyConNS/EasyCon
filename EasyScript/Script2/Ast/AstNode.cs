namespace ECP.Ast;

public abstract class AstNode
{
    public abstract T Accept<T>(IAstVisitor<T> visitor);
}