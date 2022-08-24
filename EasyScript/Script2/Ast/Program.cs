using System.Collections.ObjectModel;

namespace ECP.Ast;

public class Program : AstNode
{
    public Program(IList<Statement> statements)
    {
        Statements = new ReadOnlyCollection<Statement>(statements);
    }

    public ReadOnlyCollection<Statement> Statements { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitProgram(this);
    }
}