using ECP.Ast;
using VBF.Compilers;

namespace ECP;

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
        Console.WriteLine("program start:");
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
        Console.WriteLine(ast);
        return ast;
    }

    public override AstNode VisitConstDefine(ConstDefine ast)
    {
        Console.WriteLine($"{ast.ConstName}={ast.ConstValue}");
        return ast;
    }

    public override AstNode VisitMovStatement(MovStatement ast)
    {
        var op = ast.Negi ? "= ~": "= ";
        Console.WriteLine($"{ast.DestVar} {op}{ast.AssignExpr}");
        return ast;
    }

    public override AstNode VisitWaitExp(WaitExp ast)
    {
        Console.WriteLine($"WAIT({ast.Duration})");
        return ast;
    }

    public override AstNode VisitIfElse(IfElse ast)
    {
        Console.WriteLine("if:");
        Visit(ast.Condition);
        Visit(ast.BlockStmt);
        if(ast.ElifStmt != null)
        {
            foreach(var el in ast.ElifStmt)
                Visit(el);
        }
        if(ast.ElseStmt != null)
        {
            Console.WriteLine("else:");
            Visit(ast.ElseStmt);
        }
        Console.WriteLine("endif");
        return ast;
    }

    public override AstNode VisitElseIf(ElseIf ast)
    {
        Console.WriteLine("elif:");
        Visit(ast.Condition);
        Visit(ast.BlockStmt);
        return ast;
    }

    public override AstNode VisitBinary(Binary ast)
    {
        Console.WriteLine($"{ast.m_number} {ast.Operator} {ast.r_number}");
        return ast;
    }

    public override AstNode VisitOpAssign(OpAssign ast)
    {
        Console.WriteLine($"{ast.m_number} {ast.Operator} {ast.r_number}");
        return ast;
    }

    public override AstNode VisitForState(ForStatement ast)
    {
        Console.WriteLine($"for {ast.Desp}:");
        return ast;
    }

    public override AstNode VisitForWhile(ForWhile ast)
    {
        if(ast.Condition==null)
        {
            Console.WriteLine("for infinite:");
        }
        else
        {
            Visit(ast.Condition);
        }
        Visit(ast.BlockStmt);
        Console.WriteLine("next");
        return ast;
    }

    public override AstNode VisitLoopControl(LoopControl ast)
    {
        Console.WriteLine(ast.LoopType);
        return ast;
    }

    public override AstNode VisitButtonAction(ButtonAction ast)
    {
        Console.WriteLine($"{ast.Key}({ast.Duration})");
        return ast;
    }
    public override AstNode VisitStickAction(StickAction ast)
    {
        Console.WriteLine($"{ast.Key},{ast.Destination}({ast.Duration})");
        return ast;
    }

    public override AstNode VisitFuncState(FuncState ast)
    {
        Console.WriteLine(ast);
        return ast;
    }

    public override AstNode VisitBuildinState(BuildinState ast)
    {
        Console.WriteLine(ast.BuildinFunc);
        return ast;
    }
}