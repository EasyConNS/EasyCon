namespace EC.Script.Syntax;

public sealed partial class FunctionDeclarationSyntax : MemberSyntax
{
    internal FunctionDeclarationSyntax(SyntaxTree syntaxTree, SyntaxToken functionKeyword, SeparatedSyntaxList<VariableSyntax> parameters, StatementSyntax body) : base(syntaxTree)
    {
        Parameters = parameters;
    }

    public override TokenType Kind { get => throw new NotImplementedException(); init => throw new NotImplementedException(); }

    public SeparatedSyntaxList<VariableSyntax> Parameters { get; init; }

    public override IEnumerable<SyntaxNode> GetChildren()
    {
        throw new NotImplementedException();
    }
}
