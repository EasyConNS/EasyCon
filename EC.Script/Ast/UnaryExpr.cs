using ECScript.Syntax;

namespace ECP.Ast;

internal sealed class UnaryExpr : Expression
{
    public UnaryExpr(SyntaxNode node) : base(node)
    {
    }

    public SyntaxToken Op { get; init; }
    public Expression Expr { get; init; }

    public override AstNodeType Kind => throw new NotImplementedException();

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitUnary(this);
    }
}