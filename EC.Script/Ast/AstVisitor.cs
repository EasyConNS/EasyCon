namespace EC.Script.Ast;

internal abstract class AstVisitor : IAstVisitor<AstNode>
{
    protected AstVisitor() { }

    public AstNode Visit(AstNode ast)
    {
        return ast.Accept(this);
    }

    public virtual AstNode VisitProgram(MainProgram ast)
    {
        return ast;
    }

    public virtual AstNode VisitConstDecl(ConstDecl ast)
    {
        return ast;
    }

    public virtual AstNode VisitAssign(AssignExpr ast)
    {
        return ast;
    }

    public virtual AstNode VisitBinary(BinaryExpr ast)
    {
        return ast;
    }

    public virtual AstNode VisitUnary(UnaryExpr ast)
    {
        return ast;
    }

    public virtual AstNode VisitCall(CallExpr ast)
    {
        return ast;
    }

    public virtual AstNode VisitAugAssign(AugAssignExpr ast)
    {
        throw new NotImplementedException();
    }
}