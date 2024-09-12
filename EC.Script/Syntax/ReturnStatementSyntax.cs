namespace EC.Script.Syntax;

public sealed partial class ReturnStatementSyntax : StatementSyntax
{
    public ReturnStatementSyntax(SyntaxTree syntaxTree, SyntaxToken keyword) : base(syntaxTree)
    {
    }

    public override TokenType Kind { get => throw new NotImplementedException(); init => throw new NotImplementedException(); }

    public override IEnumerable<SyntaxNode> GetChildren()
    {
        throw new NotImplementedException();
    }
}
