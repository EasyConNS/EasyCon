namespace EasyScript;

public interface IAstVisitor<T>
{
    T VisitProgram(Statement ast);
    T VisitBlock(Block ast);
    T VisitNumber(Number ast);
    T VisitVariable(Variable ast);
    T VisitConstDefine(ConstDefine ast);
    T VisitNot(Not ast);
    T VisitBinary(Binary ast);
    T VisitAssign(Assign ast);
    T VisitOpAssign(OpAssign ast);
    T VisitWait(Wait ast);
    T VisitIfElse(IfElse ast);
    T VisitElseIf(ElseIf ast);
    T VisitForCondition(ForStatement ast);
    T VisitForWhile(ForWhile ast);
    T VisitLoopControl(LoopControl ast);
    T VisitButtonAction(ButtonAction ast);
    T VisitStickAction(StickAction ast);
    T VisitFunction(Function ast);
    T VisitCallExpression(CallExpression ast);
    T VisitBuildinState(BuildinState ast);
}

public abstract class AstVisitor : IAstVisitor<ASTNode>
{
    protected AstVisitor() { }

    public virtual ASTNode VisitProgram(Statement ast)
    {
        return ast;
    }

    public virtual ASTNode VisitBlock(Block ast)
    {
        return ast;
    }

    public virtual ASTNode VisitNumber(Number ast)
    {
        return ast;
    }

    public virtual ASTNode VisitVariable(Variable ast)
    {
        return ast;
    }

    public virtual ASTNode VisitConstDefine(ConstDefine ast)
    {
        return ast;
    }

    public virtual ASTNode VisitNot(Not ast)
    {
        return ast;
    }

    public virtual ASTNode VisitBinary(Binary ast)
    {
        return ast;
    }

    public virtual ASTNode VisitAssign(Assign ast)
    {
        return ast;
    }

    public virtual ASTNode VisitWait(Wait ast)
    {
        return ast;
    }

    public virtual ASTNode VisitIfElse(IfElse ast)
    {
        return ast;
    }

    public virtual ASTNode VisitElseIf(ElseIf ast)
    {
        return ast;
    }

    public virtual ASTNode VisitOpAssign(OpAssign ast)
    {
        return ast;
    }

    public virtual ASTNode VisitForCondition(ForStatement ast)
    {
        return ast;
    }

    public virtual ASTNode VisitForWhile(ForWhile ast)
    {
        return ast;
    }

    public virtual ASTNode VisitLoopControl(LoopControl ast)
    {
        return ast;
    }

    public virtual ASTNode VisitButtonAction(ButtonAction ast)
    {
        return ast;
    }

    public virtual ASTNode VisitStickAction(StickAction ast)
    {
        return ast;
    }
    
    public virtual ASTNode VisitFunction(Function ast)
    {
        return ast;
    }

    public virtual ASTNode VisitCallExpression(CallExpression ast)
    {
        return ast;
    }

    public virtual ASTNode VisitBuildinState(BuildinState ast)
    {
        return ast;
    }

    public ASTNode Visit(ASTNode ast)
    {
        return ast.Accept(this);
    }
}
