using EasyCon.Script2.Syntax;

namespace EasyCon.Script2;

public sealed class SimpleVisitor : AstVisitor
{
    private string indent = "";

    public override ASTNode VisitProgram(MainProgram program)
    {
        foreach (var member in program.Imports)
        {
            member.Accept(this);
        }
        foreach(var ast in program.Members)
        {
            ast.Accept(this);
        }
        return program; 
    }

    public override ASTNode VisitImport(ImportStatement ast)
    {
        Console.WriteLine($"Import: {ast.Path.Value} AS [{ast.Name}]");
        return ast;
    }

    public override ASTNode VisitAssignmentStat(AssignmentStatement ast)
    {
        ast.Variable.Accept(this);
        Console.Write($"{ast.AssignmentType} ");
        ast.Expression.Accept(this);
        Console.WriteLine();
        return ast;
    }

    public override ASTNode VisitIfStat(IfStatement ast)
    {
        indent += "  "; // Increase indentation for nested blocks

        Console.Write("If");
        //if(ast.Condition is ConditionExpression cmp)
        //    cmp.Accept(this);
        //else
        //{
            //throw new Exception("not support");
        //}
        foreach (var statement in ast.ThenBranch)
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
        if (ast.ElseBranch.Count() == 0) return ast;
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
        //if(ast.IsInfinite){
        //    Console.WriteLine("For:");
        //}else if(ast.StartValue==null && ast.EndValue != null){
        //    Console.WriteLine($"For: {ast.EndValue}");
        //}else{
        //    Console.WriteLine($"For: {ast.LoopVariable} = {ast.StartValue} To {ast.EndValue}");
        //}

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

    public override ASTNode VisitFunctionDefinition(FunctionDeclarationStatement ast)
    {
        Console.WriteLine($"Def Func: {ast.FuncDecl.NameIdent.Value}");
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
    public override ASTNode VisitKey(GamePadStatement ast)
    {
        Console.WriteLine($"Key: {ast.KeyName}");
        return ast;
    }

}