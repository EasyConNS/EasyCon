using ECScript.Syntax;

namespace ECP.Ast;

internal sealed class ElseStmt : Statement
{
    public ElseStmt(SyntaxNode syntax) : base(syntax)
    {
    }

    public override AstNodeType Kind => throw new NotImplementedException();

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        throw new NotImplementedException();
    }
}
