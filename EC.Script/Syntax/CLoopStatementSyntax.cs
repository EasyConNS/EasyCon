namespace EC.Script.Syntax;

internal sealed partial class BreakStatementSyntax : StatementSyntax
{
    public BreakStatementSyntax(SyntaxTree syntaxTree, SyntaxToken keyword) : base(syntaxTree)
    {
    }

    public override TokenType Kind { get => throw new NotImplementedException(); init => throw new NotImplementedException(); }

    public override IEnumerable<SyntaxNode> GetChildren()
    {
        throw new NotImplementedException();
    }
}

internal sealed partial class ContinueStatementSyntax : StatementSyntax
{
    public ContinueStatementSyntax(SyntaxTree syntaxTree, SyntaxToken keyword) : base(syntaxTree)
    {
    }

    public override TokenType Kind { get => throw new NotImplementedException(); init => throw new NotImplementedException(); }

    public override IEnumerable<SyntaxNode> GetChildren()
    {
        throw new NotImplementedException();
    }
}
