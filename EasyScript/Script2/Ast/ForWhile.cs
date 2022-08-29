using System.Collections.ObjectModel;

namespace ECP.Ast;

public class ForWhile : Statement
{
    public ForWhile(ForStatement forcond, IList<Statement> statements)
    {
        Condition = forcond;
        BlockStmt = new Block(statements);
    }

    public ForStatement Condition { get; init; }
    public Block BlockStmt { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitForWhile(this);
    }
}