using ECScript.Syntax;
using System.Collections.ObjectModel;

namespace ECP.Ast;

internal sealed class CallExpr : Expression
{
    public CallExpr(SyntaxNode node, SyntaxToken package, SyntaxToken methodName, IList<Expression> argList) : base(node)
    {
        Package = package;
        Arguments = new ReadOnlyCollection<Expression>(argList);
    }

    public SyntaxToken Package { get; init; }
    public ReadOnlyCollection<Expression> Arguments { get; init; }

    public override AstNodeType Kind => throw new NotImplementedException();

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitCall(this);
    }
}