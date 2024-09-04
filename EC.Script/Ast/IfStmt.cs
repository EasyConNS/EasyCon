using EC.Script.Syntax;

namespace EC.Script.Ast;

internal sealed class IfStmt : Statement
{
    public IfStmt(SyntaxNode syntax) : base(syntax)
    {
    }

    public override AstNodeType Kind => AstNodeType.IfStatement;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        throw new NotImplementedException();
    }
}