using EC.Script.Syntax;

namespace EC.Script.Ast;

internal sealed class ConstDecl : Statement
{
    public ConstDecl(SyntaxNode syntax) : base(syntax)
    {
    }

    public override AstNodeType Kind => throw new NotImplementedException();

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitConstDecl(this);
    }
}