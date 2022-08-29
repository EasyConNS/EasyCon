using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ECP.Ast;

public class Block : Statement
{
    public Block(IList<Statement> statements)
    {
        if (statements == null)
        {
            return;
        }
        Statements = new ReadOnlyCollection<Statement>(statements);
    }

    public ReadOnlyCollection<Statement> Statements { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitBlock(this);
    }

    public override void Show()
    {
        foreach(var st in Statements)
        {
            st.Show();
        }
    }
}