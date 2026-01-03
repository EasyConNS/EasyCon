namespace EasyCon.Script2.Syntax;

public interface IAstVisitor<T>
{
    T VisitProgram(MainProgram ast);
    T VisitImport(ImportStatement ast);
    T VisitAssignmentStat(AssignmentStatement ast);
    T VisitIfStat(IfStatement ast);
    T VisitElseIfClause(ElseIfClause ast);
    T VisitElseClause(ElseClause ast);
    T VisitForStat(ForStatement ast);
    T VisitBreak(BreakStatement ast);
    T VisitContinue(ContinueStatement ast);
    T VisitFunctionDefinition(FunctionDeclarationStatement ast);
    T VisitReturn(ReturnStatement ast);
    T VisitCall(CallExpression ast);
    T VisitKey(GamePadStatement ast);
}

public abstract class AstVisitor : IAstVisitor<ASTNode>
{
    protected AstVisitor() { }

    public virtual ASTNode VisitImport(ImportStatement ast)
    {
        return ast;
    }

    public virtual ASTNode VisitProgram(MainProgram ast)
    {
        return ast; 
    }

    public virtual ASTNode VisitAssignmentStat(AssignmentStatement ast)
    {
        return ast;
    }
    public virtual ASTNode VisitIfStat(IfStatement ast)
    {
        return ast;
    }
    public virtual ASTNode VisitElseIfClause(ElseIfClause ast)
    {
        return ast;
    }
    public virtual ASTNode VisitElseClause(ElseClause ast)
    {
        return ast;
    }
    public virtual ASTNode VisitForStat(ForStatement ast)
    {
        return ast;
    }
    public virtual ASTNode VisitBreak(BreakStatement ast)
    {
        return ast;
    }
    public virtual ASTNode VisitContinue(ContinueStatement ast)
    {
        return ast;
    }
    public virtual ASTNode VisitFunctionDefinition(FunctionDeclarationStatement ast)
    {
        return ast;
    }
    public virtual ASTNode VisitReturn(ReturnStatement ast)
    {
        return ast;
    }
    public virtual ASTNode VisitCall(CallExpression ast)
    {
        return ast;
    }
    public virtual ASTNode VisitKey(GamePadStatement ast)
    {
        return ast;
    }

    public ASTNode Visit(ASTNode ast)
    {
        return ast.Accept(this);
    }
}
