using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using System.Diagnostics;
namespace EasyCon.Script.Binding;

internal static class BoundFactory
{
    public static BoundBlockStatement Block(AstNode syntax, params BoundStmt[] statements)
    {
        return new BoundBlockStatement(syntax, [.. statements]);
    }
    public static BoundGotoStatement Goto(AstNode syntax, BoundLabel label)
    {
        return new BoundGotoStatement(syntax, label);
    }

    public static BoundConditionalGotoStatement GotoTrue(AstNode syntax, BoundLabel label, BoundExpr condition)
        => new(syntax, label, condition, jumpIfTrue: true);

    public static BoundConditionalGotoStatement GotoFalse(AstNode syntax, BoundLabel label, BoundExpr condition)
        => new(syntax, label, condition, jumpIfTrue: false);

    public static BoundLabelStatement Label(AstNode syntax, BoundLabel label)
    {
        return new BoundLabelStatement(syntax, label);
    }
    public static BoundNop Nop(AstNode syntax)
    {
        return new BoundNop(syntax);
    }
    public static BoundVariableDeclaration VariableDeclaration(AstNode syntax, VariableSymbol symbol, BoundExpr initializer)
    {
        return new BoundVariableDeclaration(syntax, symbol, initializer);
    }
    public static BoundVariableDeclaration ConstantDeclaration(AstNode syntax, string name, BoundExpr initializer)
        => VariableDeclarationInternal(syntax, name, initializer, isReadOnly: true);

    private static BoundVariableDeclaration VariableDeclarationInternal(AstNode syntax, string name, BoundExpr initializer, bool isReadOnly)
    {
        var local = new LocalVariableSymbol(name, isReadOnly, initializer.Type);
        return new BoundVariableDeclaration(syntax, local, initializer);
    }

    private static BoundBinaryExpression Binary(AstNode syntax, BoundExpr left, TokenType kind, BoundExpr right)
    {
        var op = BoundBinaryOperator.Bind(kind, left.Type, right.Type)!;
        return Binary(syntax, left, op, right);
    }

    private static BoundBinaryExpression Binary(AstNode syntax, BoundExpr left, BoundBinaryOperator op, BoundExpr right)
    {
        return new BoundBinaryExpression(syntax, left, op, right);
    }
    public static BoundUnaryExpression Not(AstNode syntax, BoundExpr condition)
    {
        Debug.Assert(condition.Type == ScriptType.Bool);

        var op = BoundUnaryOperator.Bind(TokenType.LogicNot, ScriptType.Bool);
        Debug.Assert(op != null);
        return new BoundUnaryExpression(syntax, op, condition);
    }
    public static BoundBinaryExpression Concat(AstNode syntax, BoundExpr left, BoundExpr right)
    => Binary(syntax, left, TokenType.BitAnd, right);
    public static BoundBinaryExpression Add(AstNode syntax, BoundExpr left, BoundExpr right)
    => Binary(syntax, left, TokenType.ADD, right);
    public static BoundBinaryExpression Less(AstNode syntax, BoundExpr left, BoundExpr right)
    => Binary(syntax, left, TokenType.LESS, right);
    public static BoundBinaryExpression LessEqual(AstNode syntax, BoundExpr left, BoundExpr right)
    => Binary(syntax, left, TokenType.LEQ, right);

    public static BoundVariableExpression Variable(AstNode syntax, VariableSymbol variable)
    {
        return new BoundVariableExpression(syntax, variable);
    }
    public static BoundLiteralExpression Literal(AstNode syntax, object literal)
    {
        Debug.Assert(literal is string || literal is bool || literal is int);

        return new BoundLiteralExpression(syntax, Value.From(literal));
    }
}