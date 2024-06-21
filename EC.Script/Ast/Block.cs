using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ECP.Ast;

public class Block : Statement
{
    public Block(IList<Statement> statements)
    {
        if (statements == null)
        {
            Statements = new ReadOnlyCollection<Statement>(new List<Statement>());
            return;
        }
        Statements = new ReadOnlyCollection<Statement>(statements);
    }

    public ReadOnlyCollection<Statement> Statements { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitBlock(this);
    }
}