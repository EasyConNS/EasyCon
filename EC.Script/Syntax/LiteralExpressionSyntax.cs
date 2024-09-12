namespace EC.Script.Syntax;

public sealed partial class LiteralExpressionSyntax : ExpressionSyntax
{
    internal LiteralExpressionSyntax(SyntaxTree syntaxTree, SyntaxToken literalToken)
            : base(syntaxTree)
    {
        LiteralToken = literalToken;
    }

    public override TokenType Kind { get => throw new NotImplementedException(); init => throw new NotImplementedException(); }

    public SyntaxToken LiteralToken { get; init; }

    public override IEnumerable<SyntaxNode> GetChildren()
    {
        throw new NotImplementedException();
    }
}
