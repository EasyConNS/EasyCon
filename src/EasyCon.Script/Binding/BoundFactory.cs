using EasyCon.Script.Parsing;

namespace EasyCon.Script.Binding;

internal static class BoundFactory
{
    public static BoundBlockStatement Block(Statement syntax, params BoundStmt[] statements)
    {
        return new BoundBlockStatement(syntax, [.. statements]);
    }
    public static BoundGotoStatement Goto(Statement syntax, BoundLabel label)
    {
        return new BoundGotoStatement(syntax, label);
    }

    public static BoundConditionalGotoStatement GotoTrue(Statement syntax, BoundLabel label, BoundExpr condition)
        => new(syntax, label, condition, jumpIfTrue: true);

    public static BoundConditionalGotoStatement GotoFalse(Statement syntax, BoundLabel label, BoundExpr condition)
        => new(syntax, label, condition, jumpIfTrue: false);

    public static BoundLabelStatement Label(Statement syntax, BoundLabel label)
    {
        return new BoundLabelStatement(syntax, label);
    }
}
