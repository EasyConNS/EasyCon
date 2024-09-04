using EC.Script.Syntax;

namespace EC.Script.Ast;
internal sealed class KeyActionStatement : Statement
{
    public KeyActionStatement(SyntaxNode syntax) : base(syntax)
    {
    }

    public override AstNodeType Kind => AstNodeType.KeyActionStatement;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        throw new NotImplementedException();
    }
}