using EasyCon.Script.Parsing;
using System.Diagnostics;
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
    public static BoundAssignStatement VariableDeclaration(Statement syntax, VariableSymbol symbol, BoundExpr initializer)
    {
        return new BoundAssignStatement(syntax, symbol, initializer);
    }
    public static BoundAssignStatement VariableDeclaration(Statement syntax, string name, BoundExpr initializer)
        => VariableDeclarationInternal(syntax, name, initializer, isReadOnly: false);

    public static BoundAssignStatement ConstantDeclaration(Statement syntax, string name, BoundExpr initializer)
        => VariableDeclarationInternal(syntax, name, initializer, isReadOnly: true);

    private static BoundAssignStatement VariableDeclarationInternal(Statement syntax, string name, BoundExpr initializer, bool isReadOnly)
    {
        var local = new LocalVariableSymbol(name, isReadOnly, initializer.Type);
        return new BoundAssignStatement(syntax, local, initializer);
    }

    public static BoundBinaryExpression Binary(ExprBase syntax, BoundExpr left, Script2.Syntax.TokenType kind, BoundExpr right)
    {
        var op = BoundBinaryOperator.Bind(kind, left.Type, right.Type)!;
        return Binary(syntax, left, op, right);
    }

    public static BoundBinaryExpression Binary(ExprBase syntax, BoundExpr left, BoundBinaryOperator op, BoundExpr right)
    {
        return new BoundBinaryExpression(syntax, left, op, right);
    }
    public static BoundBinaryExpression Add(ExprBase syntax, BoundExpr left, BoundExpr right)
    => Binary(syntax, left, Script2.Syntax.TokenType.ADD, right);
    public static BoundBinaryExpression Less(ExprBase syntax, BoundExpr left, BoundExpr right)
    => Binary(syntax, left, Script2.Syntax.TokenType.LESS, right);

    public static BoundBinaryExpression LessOrEqual(ExprBase syntax, BoundExpr left, BoundExpr right)
        => Binary(syntax, left, Script2.Syntax.TokenType.LEQ, right);

    public static BoundLiteralExpression Literal(ExprBase syntax, object literal)
    {
        Debug.Assert(literal is string || literal is bool || literal is int);

        return new BoundLiteralExpression(syntax, literal);
    }
}
