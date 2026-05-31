using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using System.Collections.Immutable;

namespace EasyCon.Script.Binding;

internal sealed class BoundWhileStatement(Statement syntax, BoundExpr condition, BoundBlockStatement body, BoundLabel breakLabel, BoundLabel continueLabel) : BoundStmt(syntax)
{
    public override BoundNodeKind Kind => BoundNodeKind.While;
    public readonly BoundExpr Condition = condition;
    public BoundBlockStatement Body = body;
    public readonly BoundLabel BreakLabel = breakLabel;
    public readonly BoundLabel ContinueLabel = continueLabel;
}

internal enum ForKind { Infinite, Static, Full }

internal sealed class BoundForStatement(
    Statement syntax,
    ForKind kind,
    VariableSymbol? variable,
    BoundExpr? lowerBound,
    BoundExpr? upperBound,
    BoundBlockStatement body,
    BoundLabel breakLabel,
    BoundLabel continueLabel) : BoundStmt(syntax)
{
    public override BoundNodeKind Kind => BoundNodeKind.ForStatement;
    public readonly ForKind Kind2 = kind;
    public readonly VariableSymbol? Variable = variable;
    public readonly BoundExpr? LowerBound = lowerBound;
    public readonly BoundExpr? UpperBound = upperBound;
    public readonly BoundBlockStatement Body = body;
    public readonly BoundLabel BreakLabel = breakLabel;
    public readonly BoundLabel ContinueLabel = continueLabel;
}

internal sealed class BoundUntilStatement(
    Statement syntax,
    BoundExpr condition,
    BoundBlockStatement body,
    BoundLabel breakLabel,
    BoundLabel continueLabel) : BoundStmt(syntax)
{
    public override BoundNodeKind Kind => BoundNodeKind.UntilStatement;
    public readonly BoundExpr Condition = condition;
    public readonly BoundBlockStatement Body = body;
    public readonly BoundLabel BreakLabel = breakLabel;
    public readonly BoundLabel ContinueLabel = continueLabel;
}

internal sealed class BoundIfStatement(
    AstNode syntax,
    BoundExpr condition,
    BoundBlockStatement body,
    ImmutableArray<(BoundExpr Condition, BoundBlockStatement Body)> elseIfs,
    BoundBlockStatement? elseBody) : BoundStmt(syntax)
{
    public override BoundNodeKind Kind => BoundNodeKind.IfStatement;
    public readonly BoundExpr Condition = condition;
    public readonly BoundBlockStatement Body = body;
    public readonly ImmutableArray<(BoundExpr Condition, BoundBlockStatement Body)> ElseIfs = elseIfs;
    public readonly BoundBlockStatement? ElseBody = elseBody;
}