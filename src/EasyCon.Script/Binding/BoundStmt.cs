using EasyCon.Script.Parsing;
using EasyCon.Script2.Binding;
using System.Collections.Immutable;
using EasyScript;

namespace EasyCon.Script.Binding;

internal sealed class BoundBlockStatement(Statement stmt, ImmutableArray<BoundStmt> statements) : BoundStmt(stmt)
{
    public ImmutableArray<BoundStmt> Statements = statements;

    public override BoundNodeKind Kind => BoundNodeKind.BlockStatement;
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
    public override BoundNodeKind Kind => BoundNodeKind.GotoStatement;
    public readonly BoundLabel Label = label;
}

internal sealed class BoundConditionalGotoStatement(Statement syntax, BoundLabel label, BoundExpr condition, bool jumpIfTrue = true) : BoundStmt(syntax)
{
    public override BoundNodeKind Kind => BoundNodeKind.ConditionGotoStatement;
    public readonly BoundLabel Label = label;
    public readonly BoundExpr Condition = condition;
    public readonly bool JumpIfTrue = jumpIfTrue;
}

internal sealed class BoundReturnStatement(Statement syntax, BoundExpr? expression) : BoundStmt(syntax)
{
    public override BoundNodeKind Kind => BoundNodeKind.Return;
    public readonly BoundExpr? Expression = expression;
}

internal sealed class BoundExprStatement(Statement syntax, BoundExpr expression) : BoundStmt(syntax)
{
    public readonly BoundExpr Expression = expression;
    public override BoundNodeKind Kind => BoundNodeKind.ExpressionStatement;
}

internal sealed class BoundVariableDeclaration(Statement syntax, VariableSymbol variable, BoundExpr expression) : BoundStmt(syntax)
{
    public readonly VariableSymbol Variable = variable;
    public readonly BoundExpr Initializer = expression;
    public override BoundNodeKind Kind => BoundNodeKind.VariableDeclaration;
}

internal class BoundKeyActStatement(Statement syntax, GamePadKey key, bool up = false) : BoundStmt(syntax)
{
    public override BoundNodeKind Kind => BoundNodeKind.KeyAction;

    public readonly GamePadKey Act = key;

    public readonly bool Up = up;
}

internal sealed class BoundKeyPressStatement(Statement syntax, GamePadKey key, BoundExpr duration) : BoundKeyActStatement(syntax, key)
{
    public readonly BoundExpr Duration = duration;
}

internal class BoundStickActStatement(Statement syntax, GamePadKey key, byte x, byte y, int degree = 1) : BoundStmt(syntax)
{
    public override BoundNodeKind Kind => BoundNodeKind.StickAction;
    public readonly GamePadKey Act = key;
    public readonly byte X = x;
    public readonly byte Y = y;
    public readonly int Degree = degree;
}

internal sealed class BoundStickPressStatement(Statement syntax, GamePadKey key, BoundExpr duration, byte x, byte y, int degree = 1) : BoundStickActStatement(syntax, key, x, y, degree)
{
    public readonly BoundExpr Duration = duration;
}