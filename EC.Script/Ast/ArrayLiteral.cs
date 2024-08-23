using ECScript.Syntax;

namespace ECP.Ast;
internal sealed class ArrayLiteral : Expression
{
    public ArrayLiteral(SyntaxNode syntax) : base(syntax)
    {
    }

    public override AstNodeType Kind => throw new NotImplementedException();

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        throw new NotImplementedException();
    }
}