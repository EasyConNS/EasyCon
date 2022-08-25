using System.Collections.ObjectModel;

namespace ECP.Ast;

public class IfElse : Statement
{
    public IfElse(Expression condition, IList<Statement> statements)
    {
        Statements = new ReadOnlyCollection<Statement>(statements);
    }

    public ReadOnlyCollection<Statement> Statements { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitIfElse(this);
    }
}