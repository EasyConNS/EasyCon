
namespace EC.Script.Syntax;

public abstract class ExpressionSyntax : SyntaxNode
{
    private protected ExpressionSyntax(SyntaxTree syntaxTree)
            : base(syntaxTree)
    {
    }
}

public abstract class StatementSyntax : ExpressionSyntax
{
    private protected StatementSyntax(SyntaxTree syntaxTree)
            : base(syntaxTree)
    {
    }
}

public abstract class MemberSyntax : SyntaxNode
{
    private protected MemberSyntax(SyntaxTree syntaxTree)
            : base(syntaxTree)
    {
    }
}

public sealed partial class GlobalStatementSyntax : MemberSyntax
{
    internal GlobalStatementSyntax(SyntaxTree syntaxTree, StatementSyntax statement)
            : base(syntaxTree)
    {
        Statement = statement;
    }

    public StatementSyntax Statement { get; init; }
    public override TokenType Kind { get => throw new NotImplementedException(); init => throw new NotImplementedException(); }

    public override IEnumerable<SyntaxNode> GetChildren()
    {
        yield return Statement;
    }
}
