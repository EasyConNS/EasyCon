namespace ECP.Ast;

public abstract class AstVisitor : IAstVisitor<AstNode>
{
    protected AstVisitor() { }

    public virtual AstNode VisitProgram(Program ast)
    {
        return ast;
    }

    public virtual AstNode VisitConstDefine(ConstDefine ast)
    {
        return ast;
    }

    public virtual AstNode VisitIfElse(IfElse ast)
    {
        return ast;
    }

    public virtual AstNode VisitForWhile(ForWhile ast)
    {
        return ast;
    }

    public AstNode Visit(AstNode ast)
    {
        return ast.Accept(this);
    }
}