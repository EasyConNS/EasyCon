namespace ECP.Ast;

public abstract class AstVisitor : IAstVisitor<AstNode>
{
    protected AstVisitor() { }

    public virtual AstNode VisitProgram(Program ast)
    {
        return ast;
    }

    public virtual AstNode VisitBlock(Block ast)
    {
        return ast;
    }

    public virtual AstNode VisitNumber(Number ast)
    {
        return ast;
    }

    public virtual AstNode VisitConstDefine(ConstDefine ast)
    {
        return ast;
    }

    public virtual AstNode VisitMovStatement(MovStatement ast)
    {
        return ast;
    }

    public virtual AstNode VisitWaitExp(WaitExp ast)
    {
        return ast;
    }

    public virtual AstNode VisitIfElse(IfElse ast)
    {
        return ast;
    }

    public virtual AstNode VisitBinary(Binary ast)
    {
        return ast;
    }

    public virtual AstNode VisitOpAssign(OpAssign ast)
    {
        return ast;
    }

    public virtual AstNode VisitForState(ForStatement ast)
    {
        return ast;
    }

    public virtual AstNode VisitForWhile(ForWhile ast)
    {
        return ast;
    }

    public virtual AstNode VisitLoopControl(LoopControl ast)
    {
        return ast;
    }

    public virtual AstNode VisitButtonAction(ButtonAction ast)
    {
        return ast;
    }

    public virtual AstNode VisitStickAction(StickAction ast)
    {
        return ast;
    }
    
    public virtual AstNode VisitFuncState(FuncState ast)
    {
        return ast;
    }

    public virtual AstNode VisitBuildinState(BuildinState ast)
    {
        return ast;
    }

    public AstNode Visit(AstNode ast)
    {
        return ast.Accept(this);
    }
}