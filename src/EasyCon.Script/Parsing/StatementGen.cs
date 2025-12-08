using EasyCon.Script2.Ast;
using EasyScript;
using EasyScript.Parsing;
using EasyScript.Statements;

namespace EasyCon.Script.Parsing;

internal class StatementGen : AstVisitor
{
    readonly Formatter _formatter;

    int address = 0;
    List<EasyScript.Parsing.Statement>  stmts = new();

    public StatementGen(Dictionary<string, int> constants, Dictionary<string, ExternalVariable> extVars)
    {
        _formatter = new(constants, extVars);
    }

    public StatementGen(Formatter fmtt)
    {
        _formatter = fmtt;
    }

    public override ASTNode VisitProgram(MainProgram program)
    {
        foreach (var ast in program.Statements)
        {
            if (ast.LeadingTrivia.Count > 0)
            {
                foreach (var trivia in ast.LeadingTrivia)
                {
                    trivia.Accept(this);
                }
            }
            ast.Accept(this);
        }
        return program;
    }

    private void cfgStatement(EasyScript.Parsing.Statement st)
    {
        // update address
        st.Address = address;
        stmts.Add(st);
        address += 1;
    }

    public override ASTNode VisitTrivia(TriviaNode ast)
    {
        var trivia = new Empty
        {
            Comment = ast.Text
        };
        cfgStatement(trivia);

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
        if (ast.Variable.IsConstant)
        {
            if(ast.Expression is LiteralExpression litExpression && litExpression.Value is uint uVal)
            {
                _formatter.TryDeclConstant(ast.Variable.Name, $"{uVal}");

                var constDecl = new Empty($"{ast.Variable.Name} = {uVal}");
                cfgStatement(constDecl);
            }
        }
        else
        {
            // assign
        }

            return ast;
    }

    public override ASTNode VisitIfStat(IfStatement ast)
    {

        Console.Write("If");
        if (ast.Condition is ConditionExpression cmp)
            cmp.Accept(this);
        else
        {
            throw new Exception("not support");
        }
        foreach (var statement in ast.ThenBranch)
        {
            statement.Accept(this);
        }
        foreach (var statement in ast.ElseIfBranch)
        {
            statement.Accept(this);
        }
        ast.ElseClause?.Accept(this);
        Console.WriteLine("Endif");

        return ast;
    }
   
    public override ASTNode VisitElseIfClause(ElseIfClause ast)
    {
        Console.Write("Else If");
        ast.Condition.Accept(this);
        foreach (var statement in ast.ElseIfBranch)
        {
            statement.Accept(this);
        }
        return ast;
    }
    
    public override ASTNode VisitElseClause(ElseClause ast)
    {
        if (ast.ElseBranch.Count() == 0) return ast;
        Console.WriteLine("Else:");
        foreach (var statement in ast.ElseBranch)
        {
            statement.Accept(this);
        }
        return ast;
    }

    public override ASTNode VisitForStat(ForStatement ast)
    {
        if (ast.IsInfinite)
        {
            Console.WriteLine("For:");
        }
        else if (ast.LoopCount != null)
        {
            Console.WriteLine($"For: {ast.LoopCount}");
        }
        else
        {
            Console.WriteLine($"For: {ast.LoopVariable} = {ast.StartValue} To {ast.EndValue}");
        }

        foreach (var statement in ast.Body)
        {
            statement.Accept(this);
        }
        Console.WriteLine("Next");
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
        Console.WriteLine($"Def Func: {ast.FunctionIdent.Value}");
        foreach (var statement in ast.Body)
        {
            statement.Accept(this);
        }
        return ast;
    }

    public override ASTNode VisitCall(CallExpression ast)
    {
        switch(ast.FunctionName.ToLower())
        {
            case "wait":
                var dur = ast.Arguments.First();
                if(dur != null)
                    cfgStatement(new Wait(50, true));

                 break;
        }
        return ast;
    }

    public override ASTNode VisitReturn(ReturnStatement ast)
    {
        Console.WriteLine("Return");
        return ast;
    }
    
    public override ASTNode VisitKey(KeyStatement ast)
    {
        if(ast is ButtonStatement btnStmt)
        {
            if(ast.Duration == 50)
            {
                cfgStatement(new KeyPress(ast.KeyName));
            }
            else
            {
                cfgStatement(new KeyPress(ast.KeyName, _formatter.GetValueEx($"{ast.Duration}")));
            }
        }
        else if(ast is ButtonStStatement btn)
        {
            if (btn.IsDown)
            {
                cfgStatement(new KeyDown(btn.KeyName));
            }
            else
            {
                cfgStatement(new KeyUp(btn.KeyName));
            }
        }
        else if(ast is StickStatement st)
        {
            if(st.IsReset)
            {
                cfgStatement(new StickUp(st.KeyName));
            }
            else if(st.Duration== 50)
            {
                cfgStatement(new StickDown(st.KeyName, st.Direction));
            }
            else
            {
                cfgStatement(new StickPress(st.KeyName, st.Direction, _formatter.GetValueEx($"{ast.Duration}")));
            }
        }

        return ast;
    }
}
