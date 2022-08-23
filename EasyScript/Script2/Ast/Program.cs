using System.Collections.ObjectModel;

namespace ECP.Ast;

public class Program : AstNode
{
    public ReadOnlyCollection<Statement> Statements { get; private set; }
    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitProgram(this);
    }
}