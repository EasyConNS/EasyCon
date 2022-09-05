using System.Collections.ObjectModel;

namespace ECP.Ast;

public class FuncState : Statement
{
    public FuncState(string name, IList<Statement> statements)
    {
        Name = name;
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