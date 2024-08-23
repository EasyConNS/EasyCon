using ECP.Ast;

namespace EC.Script;

internal sealed class SimpleVisitor : AstVisitor
{
    /*private CompilationErrorList m_errorList;
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
    */
    public override AstNode VisitProgram(MainProgram ast)
    {
        System.Diagnostics.Debug.WriteLine("program start:");
        /*foreach (var st in ast.Statements)
        {
            Visit(st);
        }*/
        return ast;
    }
}