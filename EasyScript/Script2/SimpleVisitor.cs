using ECP.Ast;

namespace ECP;

public class SimpleVisitor : AstVisitor
{
    public override AstNode VisitProgram(Program ast)
    {
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
        return ast;
    }

    public override AstNode VisitConstDefine(ConstDefine ast)
    {
        return ast;
    }

    public override AstNode VisitMovStatement(MovStatement ast)
    {
        return ast;
    }

    public override AstNode VisitWaitExp(WaitExp ast)
    {
        return ast;
    }

    public override AstNode VisitIfElse(IfElse ast)
    {
        return ast;
    }

    public override AstNode VisitBinary(Binary ast)
    {
        Console.WriteLine(ast);
        return ast;
    }

    public override AstNode VisitForState(ForStatement ast)
    {
        return ast;
    }

    public override AstNode VisitForWhile(ForWhile ast)
    {
        return ast;
    }

    public override AstNode VisitButtonAction(ButtonAction ast)
    {
        return ast;
    }

    public override AstNode VisitFuncState(FuncState ast)
    {
        return ast;
    }

    public override AstNode VisitBuildinState(BuildinState ast)
    {
        Console.WriteLine(ast);
        return ast;
    }
}