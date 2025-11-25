namespace EasyScript;

public class SimpleVisitor : AstVisitor
{
    private string indent = "";
    
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
       Console.Write($"Binary: {ast.Operator}");
       return ast;
   }

   public override ASTNode VisitCondition(ConditionExpression ast)
   {
       Console.Write($"Condition: {ast.Operator}");
       return ast;
   }

    public override ASTNode VisitAssignmentStat(AssignmentStatement ast)
    {
        Console.Write($"{ast.VariableName} {ast.AssignmentType} ");
        ast.Value.Accept(this);
        Console.WriteLine();
        return ast;
    }

    public override ASTNode VisitIfStat(IfStatement ast)
    {
        indent += "  "; // Increase indentation for nested blocks

        Console.Write("If(");
        Console.Write(ast.Condition.Accept(this));
        Console.WriteLine(")");
        foreach(var statement in ast.ThenBranch)
        {
            statement.Accept(this);
        }
        ast.ElseBranch?.Accept(this);
        Console.WriteLine("Endif");

        indent = indent.Substring(2);

        return ast;
    }
    public override ASTNode VisitElseClause(ElseClause ast)
    {
        if (ast.ElseBranch.Count == 0) return ast;
        Console.WriteLine("Else:");
        foreach(var statement in ast.ElseBranch)
        {
            statement.Accept(this);
        }
        return ast;
    }

    public override ASTNode VisitForStat(ForStatement ast)
    {
        indent += "  "; // Increase indentation for nested blocks

        Console.WriteLine("For:");
        foreach(var statement in ast.Body)
        {
            statement.Accept(this);
        }
        Console.WriteLine("Next");

        indent = indent.Substring(2);
        return ast;
    }

    public override ASTNode VisitContinue(ContinueStatement ast)
    {
        Console.WriteLine("Continue");
        return ast;
    }
    public override ASTNode VisitBreak(BreakStatement ast)
    {
        Console.WriteLine("Break");
        return ast;
    }

    public override ASTNode VisitFunctionDefinition(FunctionDefinitionStatement ast)
    {
        Console.WriteLine($"Def Func: {ast.FunctionName}");
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