using ECP.Ast;
using VBF.Compilers;

namespace EC.Script;

public class SimpleVisitor : AstVisitor
{
    private CompilationErrorList m_errorList;
    private readonly CompilationErrorManager m_errorManager;

    public SimpleVisitor(CompilationErrorManager errorManager)
    {
        m_errorManager = errorManager;
    }

    public CompilationErrorList ErrorList
    {
        get { return m_errorList; }
        set { m_errorList = value; }
    }

    public override AstNode VisitProgram(Program ast)
    {
        System.Diagnostics.Debug.WriteLine("program start:");
        foreach (var st in ast.Statements)
        {
            Visit(st);
        }
        return ast;
    }

    public override AstNode VisitBlock(Block ast)
    {
        foreach (var st in ast.Statements)
        {
            Visit(st);
        }
        return ast;
    }
    public override AstNode VisitNumber(Number ast)
    {
        System.Diagnostics.Debug.WriteLine(ast);
        return ast;
    }

    public override AstNode VisitConstDefine(ConstDefine ast)
    {
        System.Diagnostics.Debug.WriteLine($"{ast.ConstName}={ast.ConstValue}");
        return ast;
    }

    public override AstNode VisitAssign(Assign ast)
    {
        System.Diagnostics.Debug.WriteLine($"{ast.SufExpr} = {ast.AssignExpr}");
        return ast;
    }

    public override AstNode VisitOpAssign(OpAssign ast)
    {
        System.Diagnostics.Debug.WriteLine($"{ast.Variable} {ast.Operator}= {ast.AssignExpr}");
        return ast;
    }

    public override AstNode VisitBinary(Binary ast)
    {
        System.Diagnostics.Debug.WriteLine($"{ast.Left} {ast.Operator} {ast.Right}");
        return ast;
    }

    public override AstNode VisitIfElse(IfElse ast)
    {
        System.Diagnostics.Debug.WriteLine("if:");
        Visit(ast.Condition);
        Visit(ast.TrueBlock);
        if(ast.ElifStmt != null)
        {
            foreach(var el in ast.ElifStmt)
                Visit(el);
        }
        if(ast.ElseStmt != null)
        {
            System.Diagnostics.Debug.WriteLine("else:");
            Visit(ast.ElseStmt);
        }
        System.Diagnostics.Debug.WriteLine("endif");
        return ast;
    }

    public override AstNode VisitElseIf(ElseIf ast)
    {
        System.Diagnostics.Debug.WriteLine("elif:");
        Visit(ast.Condition);
        Visit(ast.BlockStmt);
        return ast;
    }

    public override AstNode VisitForCondition(ForStatement ast)
    {

        if (ast is ForStatementFull astFull)
        {
            System.Diagnostics.Debug.WriteLine($"for {astFull.Start}:");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"for {ast.LoopCount}:");
        }
        return ast;
    }

    public override AstNode VisitForWhile(ForWhile ast)
    {
        if(ast.Condition==null)
        {
            System.Diagnostics.Debug.WriteLine("for infinite:");
        }
        else
        {
            Visit(ast.Condition);
        }
        Visit(ast.BlockStmt);
        System.Diagnostics.Debug.WriteLine("next");
        return ast;
    }

    public override AstNode VisitLoopControl(LoopControl ast)
    {
        System.Diagnostics.Debug.WriteLine(ast.LoopType);
        return ast;
    }

    public override AstNode VisitButtonAction(ButtonAction ast)
    {
        System.Diagnostics.Debug.WriteLine($"{ast.Key}({ast.Duration})");
        return ast;
    }
    public override AstNode VisitStickAction(StickAction ast)
    {
        System.Diagnostics.Debug.WriteLine($"{ast.Key},{ast.Destination}({ast.Duration})");
        return ast;
    }

    public override AstNode VisitFunction(Function ast)
    {
        System.Diagnostics.Debug.WriteLine(ast);
        return ast;
    }
}