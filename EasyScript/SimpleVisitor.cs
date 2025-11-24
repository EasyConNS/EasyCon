namespace EasyScript;

public class SimpleVisitor : AstVisitor
{
    public override ASTNode VisitLiteral(LiteralExpression ast)
    {
         Console.Write($"{ast.Value}");
         return ast;
         
    }
    
    public override ASTNode VisitVariable(VariableExpression ast)
    {
        Console.Write($"{ast.Name}");
        return ast;
    }
    
   public override ASTNode VisitBinaryOp(BinaryExpression ast)
   {
       Console.WriteLine($"Binary: {ast.Operator}");
       return ast;
   }

    public override ASTNode VisitAssignmentStat(AssignmentStatement ast)
    {
        Console.WriteLine($"Assign: {ast.VariableName}");
        return ast;
    }

    public override ASTNode VisitIfStat(IfStatement ast)
    {
        Console.WriteLine("If:");
        Console.WriteLine(ast.Condition.Accept(this));
        foreach(var statement in ast.ThenBranch)
        {
            statement.Accept(this);
        }
        ast.ElseBranch?.Accept(this);
        Console.WriteLine("Endif");
        return ast;
    }
    public override ASTNode VisitElseClause(ElseClause ast)
    {
        Console.WriteLine("Else:");
        foreach(var statement in ast.ElseBranch)
        {
            statement.Accept(this);
        }
        return ast;
    }

    public override ASTNode VisitForStat(ForStatement ast)
    {
        Console.WriteLine("For:");
        foreach(var statement in ast.Body)
        {
            statement.Accept(this);
        }
        Console.WriteLine("Next");
        return ast;
    }

    public override ASTNode VisitFunctionDefinition(FunctionDefinitionStatement ast)
    {
        Console.WriteLine($"Function: {ast.FunctionName}");
        return ast;
    }

    public override ASTNode VisitCall(CallExpression ast)
    {
        Console.Write($"Call: {ast.FunctionName} ");
        foreach(var arg in ast.Arguments)
        {
            arg.Accept(this);
        }
        Console.WriteLine();
        return ast;
    }
    
    public override ASTNode VisitReturn(ReturnStatement ast)
    {
        Console.WriteLine("Return");
        return ast;
    }
    public override ASTNode VisitKey(KeyStatement ast)
    {
        Console.WriteLine($"Key: {ast.KeyName}");
        return ast;
    }

}