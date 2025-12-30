using EasyScript.Statements;

namespace EasyScript.Binding;

internal sealed class BoundLabel(string name)
{
    public readonly string Name = name;

    public override string ToString() => Name;
}
internal sealed class BoundGotoStatement(Statement syntax, BoundLabel label) : BoundStmt(syntax)
{
    public override BoundNodeKind Kind => BoundNodeKind.Goto;
    public readonly BoundLabel Label = label;
}

internal sealed class BoundConditionalGotoStatement(Statement syntax, BoundLabel label, /*BoundExpression condition,*/ bool jumpIfTrue = true) : BoundStmt(syntax)
{
    public override BoundNodeKind Kind => BoundNodeKind.ConditionGoto;
    public readonly BoundLabel Label = label;
    //public readonly BoundExpression Condition;
    public readonly bool JumpIfTrue = jumpIfTrue;
}