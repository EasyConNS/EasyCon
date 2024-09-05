
namespace EC.Script.Syntax;

public sealed partial class AssignmentStatement : StatementSyntax
{
    public AssignmentStatement(ExpressionSyntax expression)
    {
        Expression = expression;
    }

    public override TokenType Kind { get => throw new NotImplementedException(); init => throw new NotImplementedException(); }

    public ExpressionSyntax Expression { get; init; }

    public override IEnumerable<SyntaxNode> GetChildren()
    {
        throw new NotImplementedException();
    }
}
