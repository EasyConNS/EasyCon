using System.CodeDom.Compiler;
using EasyCon.Script2.Ast;
using EasyCon.Script2.Binding;

public class AstFormatter : AstVisitor
{
    private readonly IndentedTextWriter _writer;

    public AstFormatter(TextWriter writer)
    {
        _writer = new IndentedTextWriter(writer, "    ");
    }

    public string Print(ASTNode node)
    {
        node.Accept(this);
        return _writer.InnerWriter.ToString();
    }

    public override ASTNode VisitProgram(MainProgram program)
    {
        foreach (var statement in program.Members)
        {
            statement.Accept(this);
        }
        return program;
    }

    public override ASTNode VisitAssignmentStat(AssignmentStatement assignment)
    {
        WriteLeadingTrivia(assignment);
        _writer.Write($"{assignment.Variable.Name} {BoundNodePrinter.GetOperatorText(assignment.AssignmentType)} ");
        assignment.Expression.Accept(this);
        WriteTrailingTrivia(assignment);
        _writer.WriteLine();
        return assignment;
    }

    public override ASTNode VisitIfStat(IfStatement ifStatement)
    {
        WriteLeadingTrivia(ifStatement);
        _writer.Write("IF ");
        ifStatement.Condition.Accept(this);
        WriteTrailingTrivia(ifStatement.Condition);
        _writer.WriteLine();
        
        _writer.Indent++;
        foreach (var statement in ifStatement.ThenBranch)
        {
            statement.Accept(this);
        }
        _writer.Indent--;
        
        foreach (var elseif in ifStatement.ElseIfBranch)
        {
            elseif.Accept(this);
        }
        
        if (ifStatement.ElseClause != null)
        {
            ifStatement.ElseClause.Accept(this);
        }
        
        _writer.Write("ENDIF");
        WriteTrailingTrivia(ifStatement);
        _writer.WriteLine();
        
        return ifStatement;
    }

    public override ASTNode VisitElseIfClause(ElseIfClause elseIfClause)
    {
        WriteLeadingTrivia(elseIfClause);
        _writer.Write("ELIF ");
        elseIfClause.Condition.Accept(this);
        WriteTrailingTrivia(elseIfClause.Condition);
        _writer.WriteLine();
        
        _writer.Indent++;
        foreach (var statement in elseIfClause.ElseIfBranch)
        {
            statement.Accept(this);
        }
        _writer.Indent--;
        return elseIfClause;
    }

    public override ASTNode VisitElseClause(ElseClause elseClause)
    {
        WriteLeadingTrivia(elseClause);
        _writer.Write("ELSE");
        WriteTrailingTrivia(elseClause);
        _writer.WriteLine();
        
        _writer.Indent++;
        foreach (var statement in elseClause.ElseBranch)
        {
            statement.Accept(this);
        }
        _writer.Indent--;
        return elseClause;
    }

    public override ASTNode VisitForStat(ForStatement forStatement)
    {
        WriteLeadingTrivia(forStatement);
        _writer.Write("FOR ");
        
        //if (forStatement.IsInfinite)
        //{
        //    // Infinite loop
        //}
        //else if (forStatement.StartValue == null && forStatement.EndValue != null)
        //{
        //    // Counted loop
        //    forStatement.EndValue.Accept(this);
        //}
        //else
        //{
        //    // Range loop
        //    _writer.Write($"{forStatement.LoopVariable.Name} = ");
        //    forStatement.StartValue.Accept(this);
        //    _writer.Write(" TO ");
        //    forStatement.EndValue.Accept(this);
            
        //    if (forStatement.StepValue != 1)
        //    {
        //        _writer.Write($" STEP {forStatement.StepValue}");
        //    }
        //}
        
        _writer.WriteLine();
        
        _writer.Indent++;
        foreach (var statement in forStatement.Body)
        {
            statement.Accept(this);
        }
        _writer.Indent--;
        
        _writer.Write("NEXT");
        WriteTrailingTrivia(forStatement);
        _writer.WriteLine();
        return forStatement;
    }

    public override ASTNode VisitBreak(BreakStatement breakStatement)
    {
        WriteLeadingTrivia(breakStatement);
        if (breakStatement.Circle > 1)
        {
            _writer.Write($"BREAK {breakStatement.Circle}");
        }
        else
        {
            _writer.Write("BREAK");
        }
        WriteTrailingTrivia(breakStatement);
        _writer.WriteLine();
        return breakStatement;
    }

    public override ASTNode VisitContinue(ContinueStatement continueStatement)
    {
        WriteLeadingTrivia(continueStatement);
        _writer.Write("CONTINUE");
        WriteTrailingTrivia(continueStatement);
        _writer.WriteLine();
        return continueStatement;
    }

    public override ASTNode VisitFunctionDefinition(FunctionDeclarationStatement functionDef)
    {
        WriteLeadingTrivia(functionDef);
        _writer.Write($"FUNC {functionDef.FuncDecl.NameIdent.Value}()");
        _writer.WriteLine();
        
        _writer.Indent++;
        foreach (var statement in functionDef.Body)
        {
            statement.Accept(this);
        }
        _writer.Indent--;
        
        _writer.WriteLine("ENDFUNC");
        return functionDef;
    }

    public override ASTNode VisitReturn(ReturnStatement returnStatement)
    {
        WriteLeadingTrivia(returnStatement);
        _writer.Write("RETURN");
        if (returnStatement.Value != null)
        {
            _writer.Write(" ");
            returnStatement.Value.Accept(this);
        }
        WriteTrailingTrivia(returnStatement);
        _writer.WriteLine();
        return returnStatement;
    }

    public override ASTNode VisitCall(CallExpression call)
    {
        WriteLeadingTrivia(call);
        _writer.Write($"{call.FunctionName}(");
        for (int i = 0; i < call.Arguments.Length; i++)
        {
            if (i > 0) _writer.Write(", ");
            call.Arguments[i].Accept(this);
        }
        _writer.Write(")");
        WriteTrailingTrivia(call);
        _writer.WriteLine();
        return call;
    }

    public override ASTNode VisitKey(GamePadStatement keyStatement)
    {
        WriteLeadingTrivia(keyStatement);
        _writer.Write(keyStatement.KeyName);
        
        if (keyStatement is ButtonStatement button)
        {
            if (button.Duration != 50) // Default duration
            {
                _writer.Write($" {button.Duration}");
            }
        }
        else if (keyStatement is ButtonStStatement buttonSt)
        {
            _writer.Write($" {(buttonSt.IsDown ? "DOWN" : "UP")}");
        }
        else if (keyStatement is StickStatement stick)
        {
            if (stick.IsReset)
            {
                _writer.Write(" RESET");
            }
            else if (!string.IsNullOrEmpty(stick.Direction))
            {
                _writer.Write($" {stick.Direction}");
                if (stick.Duration != 50) // Default duration
                {
                    _writer.Write($", {stick.Duration}");
                }
            }
        }
        
        WriteTrailingTrivia(keyStatement);
        _writer.WriteLine();
        return keyStatement;
    }

    private void WriteLeadingTrivia(Statement statement)
    {
        foreach (var trivia in statement.LeadingTrivia)
        {
            trivia.Accept(this);
        }
    }

    private void WriteLeadingTrivia(Member statement)
    {
        foreach (var trivia in statement.LeadingTrivia)
        {
            trivia.Accept(this);
        }
    }

    private void WriteTrailingTrivia(Expression statement)
    {
        foreach (var trivia in statement.TrailingTrivia)
        {
            _writer.Write($" {trivia.Text}");
        }
    }

    private void WriteTrailingTrivia(Statement statement)
    {
        foreach (var trivia in statement.TrailingTrivia)
        {
            _writer.Write($" {trivia.Text}");
        }
    }
}