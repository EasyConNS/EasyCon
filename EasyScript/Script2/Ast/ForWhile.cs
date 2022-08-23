using System.Collections.ObjectModel;

namespace ECP.Ast;

public class ForWhile : Statement
{
    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitForWhile(this);
    }
}