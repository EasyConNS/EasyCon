using EasyCon.Script.Parsing;
using EasyCon.Script2.Binding;
using EasyDevice;
using System.Collections.Immutable;

namespace EasyCon.Script.Binding;

internal sealed class BoundBlockStatement(Statement stmt, ImmutableArray<BoundStmt> statements) : BoundStmt(stmt)
{
    public ImmutableArray<BoundStmt> Statements = statements;

    public override BoundNodeKind Kind => BoundNodeKind.BlockStatement;
}


internal sealed class BoundAssignStatement(Statement syntax, VariableSymbol variable, BoundExpr expression) :BoundStmt(syntax)
{
    public override BoundNodeKind Kind => BoundNodeKind.ExpressionStatement;
    public readonly VariableSymbol Variable = variable;
    public readonly BoundExpr Expression = expression;
}

internal sealed class BoundNop(Statement syntax) : BoundStmt(syntax)
{
    public override BoundNodeKind Kind => BoundNodeKind.NopStatement;
}

internal sealed class BoundLabelStatement(Statement syntax, BoundLabel label) : BoundStmt(syntax)
{
    public override BoundNodeKind Kind => BoundNodeKind.Label;
    public readonly BoundLabel Label = label;
}

internal sealed class BoundGotoStatement(Statement syntax, BoundLabel label) : BoundStmt(syntax)
{
    public override BoundNodeKind Kind => BoundNodeKind.Goto;
    public readonly BoundLabel Label = label;
}

internal sealed class BoundConditionalGotoStatement(Statement syntax, BoundLabel label, BoundExpr condition, bool jumpIfTrue = true) : BoundStmt(syntax)
{
    public override BoundNodeKind Kind => BoundNodeKind.ConditionGoto;
    public readonly BoundLabel Label = label;
    public readonly BoundExpr Condition = condition;
    public readonly bool JumpIfTrue = jumpIfTrue;
}

internal sealed class BoundReturnStatement(Statement syntax) : BoundStmt(syntax)
{
    public override BoundNodeKind Kind => BoundNodeKind.Return;
}

internal sealed class BoundCallStatement(Statement syntax, BoundCallExpression expression) : BoundStmt(syntax)
{
    public BoundCallExpression Expression = expression;
    public override BoundNodeKind Kind => BoundNodeKind.CallStatement;
}

internal class BoundKeyActStatement(Statement syntax, ECKey key, bool up = false) : BoundStmt(syntax)
{
    public override BoundNodeKind Kind => BoundNodeKind.KeyAction;

    public readonly ECKey Act = key;

    public readonly bool Up = up;
}

internal sealed class BoundKeyPressStatement(Statement syntax, ECKey key, BoundExpr duration) : BoundKeyActStatement(syntax, key)
{
    public readonly BoundExpr Duration = duration;
}