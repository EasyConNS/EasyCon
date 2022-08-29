using System.Collections.ObjectModel;

namespace ECP.Ast;

public class IfElse : Statement
{
    public IfElse(Binary condition, IList<Statement> statements)
    {
        Condition = condition;
        BlockStmt = new Block(statements);
    }

    public Binary Condition { get; init; }
    public Block BlockStmt { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitIfElse(this);
    }

    public override void Show()
    {
        Console.WriteLine($"{this}:{Condition} - start");
        BlockStmt.Show();
        Console.WriteLine($"{this} - end");
    }
}