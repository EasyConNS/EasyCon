using EasyScript.Statements;

namespace EasyScript.Binding;

internal sealed class BoundFor(Statement syntax) : BoundStmt(syntax)
{
    public override BoundNodeKind Kind => BoundNodeKind.For;
}