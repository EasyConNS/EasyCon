using EC.Script.Syntax;

namespace EC.Script.Ast;

internal sealed class AugAssignExpr : Expression
{
    public AugAssignExpr(SyntaxNode node, SyntaxNode target, SyntaxNode op, Expression expr) : base(node)
    {
        Target = target;
        Operator = op;
        Expr = expr;
    }

    public SyntaxNode Target { get; init; }
    public SyntaxNode Operator { get; init; }
    public Expression Expr { get; init; }

    public override AstNodeType Kind => AstNodeType.CompoundAssignmentExpression;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitAugAssign(this);
    }
}
