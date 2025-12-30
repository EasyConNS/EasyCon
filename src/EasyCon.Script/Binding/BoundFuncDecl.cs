using EasyScript.Statements;

namespace EasyScript.Binding;

internal sealed class BoundFuncDecl(Statement syntax) : BoundStmt(syntax)
{
    public override BoundNodeKind Kind => BoundNodeKind.Func;
}