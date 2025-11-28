using EC.Script.Ast;

namespace EC.Script;

public sealed class SimpleVisitor : AstVisitor
{
    private string indent = "";

    public override ASTNode VisitProgram(MainProgram program)
    {
        foreach(var ast in program.Statements)
        {
            if(ast.LeadingTrivia.Count > 0)
            {
                foreach(var trivia in ast.LeadingTrivia)
                {
                    trivia.Accept(this);
                }
            }
            ast.Accept(this);
        }
        return program; 
    }

    public override ASTNode VisitTrivia(TriviaNode ast)
    {
        Console.WriteLine(ast.Text);
        return ast;
    }
    
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
       Console.Write("(");
       ast.Left.Accept(this);
       Console.Write($" {ast.Operator} ");
       ast.Right.Accept(this);
       Console.Write(")");

       return ast;
   }

   public override ASTNode VisitCondition(ConditionExpression ast)
   {
       Console.Write("(");
       ast.Left.Accept(this);
       Console.Write($" {ast.Operator} ");
       ast.Right.Accept(this);
       Console.WriteLine(")");
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

        Console.Write("If");
        ast.Condition.Accept(this);
        foreach(var statement in ast.ThenBranch)
        {
            statement.Accept(this);
        }
        foreach(var statement in ast.ElseIfBranch)
        {
            statement.Accept(this);
        }
        ast.ElseClause?.Accept(this);
        Console.WriteLine("Endif");

        indent = indent.Substring(2);

        return ast;
    }
    public override ASTNode VisitElseIfClause(ElseIfClause ast)
    {
        Console.Write("Else If");
        ast.Condition.Accept(this);
        foreach(var statement in ast.ElseIfBranch)
        {
            statement.Accept(this);
        }
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
        if(ast.IsInfinite){
            Console.WriteLine("For:");
        }else if(ast.LoopCount != null){
            Console.WriteLine($"For: {ast.LoopCount}");
        }else{
            Console.WriteLine($"For: {ast.LoopVariable} = {ast.StartValue} To {ast.EndValue}");
        }

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
        Console.WriteLine($"CONTINIUE");
        return ast;
    }
    public override ASTNode VisitBreak(BreakStatement ast)
    {
        string circle = ast.Circle > 1 ? $" {ast.Circle}" : "";
        Console.WriteLine($"BREAK{circle}");
        return ast;
    }

    public override ASTNode VisitFunctionDefinition(FunctionDefinitionStatement ast)
    {
        Console.WriteLine($"Def Func: {ast.FunctionName}");
        foreach(var statement in ast.Body)
        {
            statement.Accept(this);
        }
        return ast;
    }

    public override ASTNode VisitCall(CallExpression ast)
    {
        Console.Write($"F:{ast.FunctionName} ");
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