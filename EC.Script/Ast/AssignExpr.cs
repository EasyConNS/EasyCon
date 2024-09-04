using EC.Script.Syntax;

namespace EC.Script.Ast;

internal sealed class AssignExpr : Statement
{
    public AssignExpr(SyntaxNode syntax, Expression sufexpr, Expression resexpr)
        : base(syntax)
    {
        Variable = sufexpr;
        Expression = resexpr;
    }

    public Expression Variable { get; init; }
    public Expression Expression { get; init; }

    public override AstNodeType Kind => AstNodeType.AssignmentExpression;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitAssign(this);
    }
}