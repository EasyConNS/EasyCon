namespace EasyCon.Script.Syntax;

public interface IAstVisitor<T>
{
    T VisitMember(Member ast);
    T VisitExpression(Expression ast);
}

public abstract class AstVisitor : IAstVisitor<AstNode>
{
    protected AstVisitor() { }

    public AstNode Visit(AstNode ast)
    {
        return ast.Accept(this);
    }

    public AstNode VisitExpression(Expression ast)
    {
        throw new NotImplementedException();
    }

    public AstNode VisitMember(Member ast)
    {
        throw new NotImplementedException();
    }

}