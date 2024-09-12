namespace EC.Script.Syntax;

public sealed partial class UnaryExpressionSyntax : ExpressionSyntax
{
    internal UnaryExpressionSyntax(SyntaxTree syntaxTree, SyntaxToken operatorToken, ExpressionSyntax operand)
            : base(syntaxTree)
    {
        OperatorToken = operatorToken;
        Operand = operand;
    }

    public override TokenType Kind { get => throw new NotImplementedException(); init => throw new NotImplementedException(); }

    public SyntaxToken OperatorToken { get; }
    public ExpressionSyntax Operand { get; }

    public override IEnumerable<SyntaxNode> GetChildren()
    {
        throw new NotImplementedException();
    }
}
