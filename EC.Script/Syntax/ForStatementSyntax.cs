namespace EC.Script.Syntax;

public sealed partial class ForStatementSyntax : StatementSyntax
{
    internal ForStatementSyntax(SyntaxTree syntaxTree) : base(syntaxTree) { }

    public override TokenType Kind { get => throw new NotImplementedException(); init => throw new NotImplementedException(); }

    public override IEnumerable<SyntaxNode> GetChildren()
    {
        throw new NotImplementedException();
    }
}
