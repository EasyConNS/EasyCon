using ECScript.Syntax;

namespace ECP.Ast;

internal abstract class AstNode
{
    protected AstNode(SyntaxNode syntax)
    {
        Syntax = syntax;
    }

    public abstract AstNodeType Kind { get; }
    public SyntaxNode Syntax { get; init; }

    public abstract T Accept<T>(IAstVisitor<T> visitor);
}

internal abstract class Statement : AstNode
{
    protected Statement(SyntaxNode syntax) :base(syntax) { }
}

internal abstract class Expression : Statement
{
    protected Expression(SyntaxNode syntax) : base(syntax) { }
}