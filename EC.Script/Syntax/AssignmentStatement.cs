
namespace EC.Script.Syntax;

public sealed partial class AssignmentStatement : StatementSyntax
{
    public AssignmentStatement(SyntaxTree syntaxTree, SyntaxToken identifierToken, SyntaxToken assignmentToken, ExpressionSyntax expression) : base(syntaxTree)
    {
        AssignmentToken = assignmentToken;
        Expression = expression;
    }

    public override TokenType Kind { get => throw new NotImplementedException(); init => throw new NotImplementedException(); }

    public SyntaxToken AssignmentToken { get; init; }

    public ExpressionSyntax Expression { get; init; }

    public override IEnumerable<SyntaxNode> GetChildren()
    {
        throw new NotImplementedException();
    }
}
