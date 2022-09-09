using VBF.Compilers.Scanners;
using System.Collections.ObjectModel;

namespace ECP.Ast;

public class Function : Statement
{
    public Function(LexemeValue func, IList<Statement> statements)
    {
        Name = func.Content;
        if (statements == null)
        {
            Statements = new ReadOnlyCollection<Statement>(new List<Statement>());
            return;
        }
        Statements = new ReadOnlyCollection<Statement>(statements);
    }

    public string Name { get; init; }
    public ReadOnlyCollection<Statement> Statements { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitFunction(this);
    }
}

public class CallExpression : Expression
{
    public CallExpression(LexemeValue func)
    {
        Name = func.Content;
    }

    public string Name { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitCallExpression(this);
    }
}