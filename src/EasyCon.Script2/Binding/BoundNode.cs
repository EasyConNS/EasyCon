using EasyCon.Script2.Ast;

namespace EasyCon.Script2.Binding;

internal abstract class BoundNode(ASTNode syntax)
{
    public ASTNode Syntax { get; } = syntax;

    public abstract BoundNodeKind Kind { get; }

    public override string ToString()
    {
        using var writer = new StringWriter();
        this.WriteTo(writer);
        return writer.ToString();
    }
}

internal abstract class BoundStatement(ASTNode syntax) : BoundNode(syntax) { }