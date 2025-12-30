using EasyScript.Statements;

namespace EasyScript.Binding;

internal sealed class BoundIf(Statement syntax) : BoundStmt(syntax)
{
    public override BoundNodeKind Kind => BoundNodeKind.If;
}