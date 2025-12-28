namespace EasyCon.Script2.Ast;

public interface IAstVisitor<T>
{
    T VisitTrivia(TriviaNode ast);
    T VisitProgram(MainProgram ast);
    T VisitLiteral(LiteralExpression ast);
    T VisitVariable(VariableExpression ast);
    T VisitIndexExpr(IndexExpression ast);
    T VisitBinaryOp(BinaryExpression ast);
    T VisitCondition(ConditionExpression ast);
    T VisitAssignmentStat(AssignmentStatement ast);
    T VisitIfStat(IfStatement ast);
    T VisitElseIfClause(ElseIfClause ast);
    T VisitElseClause(ElseClause ast);
    T VisitForStat(ForStatement ast);
    T VisitBreak(BreakStatement ast);
    T VisitContinue(ContinueStatement ast);
    T VisitFunctionDefinition(FunctionDefinitionStatement ast);
    T VisitReturn(ReturnStatement ast);
    T VisitCall(CallExpression ast);
    T VisitKey(GamePadStatement ast);
}

public abstract class AstVisitor : IAstVisitor<ASTNode>
{
    protected AstVisitor() { }

    public virtual ASTNode VisitTrivia(TriviaNode ast)
    {
        return ast;
    }

    public virtual ASTNode VisitProgram(MainProgram ast)
    {
        return ast; 
    }

    public virtual ASTNode VisitLiteral(LiteralExpression ast)
    {
        return ast;
    }
    public virtual ASTNode VisitVariable(VariableExpression ast)
    {
        return ast;
    }
    public virtual ASTNode VisitIndexExpr(IndexExpression ast)
    {
        return ast;
    }
    public virtual ASTNode VisitBinaryOp(BinaryExpression ast)
    {
        return ast;
    }
    public virtual ASTNode VisitCondition(ConditionExpression ast)
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
    public virtual ASTNode VisitFunctionDefinition(FunctionDefinitionStatement ast)
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
