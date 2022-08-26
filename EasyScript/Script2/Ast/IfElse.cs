using System.Collections.ObjectModel;

namespace ECP.Ast;

public class IfElse : Statement
{
    public IfElse(Binary condition, IList<Statement> statements)
    {
        Condition = condition;
        Statements = new ReadOnlyCollection<Statement>(statements);
    }

    public Binary Condition { get; init; }
    public ReadOnlyCollection<Statement> Statements { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitIfElse(this);
    }

    public override void Show()
    {
        Console.WriteLine($"{this}:{Condition} - start");
        foreach(var st in Statements)
        {
            st.Show();
        }
        Console.WriteLine($"{this} - end");
    }
}