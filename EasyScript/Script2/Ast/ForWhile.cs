using System.Collections.ObjectModel;

namespace ECP.Ast;

public class ForWhile : Statement
{
    public ForWhile(IList<Statement> statements)
    {
        Statements = new ReadOnlyCollection<Statement>(statements);
    }

    public ReadOnlyCollection<Statement> Statements { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitForWhile(this);
    }

    public override void Show()
    {
        Console.WriteLine($"{this} - start");
        foreach(var st in Statements)
        {
            st.Show();
        }
        Console.WriteLine($"{this} - end");
    }
}