namespace EasyCon.Script.Syntax;

internal interface IAstVisitor<T>
{
    T VisitMember(Member ast);
    T VisitExpression(BaseExpr ast);
}

internal abstract class AstVisitor : IAstVisitor<AstNode>
{
    protected AstVisitor() { }

    public AstNode Visit(AstNode ast)
    {
        return ast.Accept(this);
    }

    public AstNode VisitExpression(BaseExpr ast)
    {
        throw new NotImplementedException();
    }

    public AstNode VisitMember(Member ast)
    {
        throw new NotImplementedException();
    }

}