using ECScript.Syntax;

namespace ECP.Ast;

internal sealed class MainProgram : AstNode
{
    public MainProgram(SyntaxNode syntax) : base(syntax)
    {
    }

    public override AstNodeType Kind => throw new NotImplementedException();

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitProgram(this);
    }
}
