using EasyScript.Statements;

namespace EasyScript.Binding;

internal abstract class BoundStmt(Statement syntax)
{
    public abstract BoundNodeKind Kind { get; }
    public readonly Statement Syntax = syntax;
}

internal abstract class BoundBlockStmt(Statement syntax) : BoundStmt(syntax)
{
    public override BoundNodeKind Kind => BoundNodeKind.BlockStatement;
}

internal sealed class BoundReturnStatement(Statement syntax) : BoundStmt(syntax)
{
    public override BoundNodeKind Kind => BoundNodeKind.Return;
}