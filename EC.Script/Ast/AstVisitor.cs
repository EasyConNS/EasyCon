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

    public virtual AstNode VisitVariable(Variable ast)
    {
        return ast;
    }

    public virtual AstNode VisitConstDefine(ConstDecl ast)
    {
        return ast;
    }

    public virtual AstNode VisitNot(Unary ast)
    {
        return ast;
    }

    public virtual AstNode VisitBinary(Binary ast)
    {
        return ast;
    }

    public virtual AstNode VisitUnary(Unary ast)
    {
        return ast;
    }

    public virtual AstNode VisitAssign(Assign ast)
    {
        return ast;
    }

    public virtual AstNode VisitCall(Call ast)
    {
        return ast;
    }

    public virtual AstNode VisitIfElse(IfElse ast)
    {
        return ast;
    }

    public virtual AstNode VisitElseIf(ElseIf ast)
    {
        return ast;
    }

    public virtual AstNode VisitOpAssign(OpAssign ast)
    {
        return ast;
    }

    public virtual AstNode VisitForCondition(ForStatement ast)
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
    
    public virtual AstNode VisitFunction(FuncDecl ast)
    {
        return ast;
    }

    public virtual AstNode VisitBooleanLiteral(BooleanLiteral ast)
    {
        return ast;
    }

    public AstNode Visit(AstNode ast)
    {
        return ast.Accept(this);
    }
}