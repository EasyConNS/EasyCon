using EC.Script.Ast;

namespace EC.Script;

internal sealed class EvalVisitor : AstVisitor
{
    private readonly MainProgram _program;
    public EvalVisitor(MainProgram program)
    {
        _program = program;
    }

    public void Evaluate()
    {
        VisitProgram(_program);
    }

    public override AstNode VisitProgram(MainProgram ast)
    {
        System.Diagnostics.Debug.WriteLine("program start:");
        return ast;
    }
}