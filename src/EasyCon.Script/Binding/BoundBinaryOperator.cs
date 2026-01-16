using EasyCon.Script2.Syntax;

namespace EasyCon.Script.Binding;

internal sealed class BoundBinaryOperator
{
    public TokenType TypeKind { get; }
    public BoundBinaryOperatorKind Kind { get; }
    public ValueType LeftType { get; }
    public ValueType RightType { get; }
    public ValueType Type { get; }

    public readonly Func<object, object, object> Operate;

    private BoundBinaryOperator(TokenType syntaxKind, BoundBinaryOperatorKind kind, ValueType type, Func<object, object, object> operate)
    : this(syntaxKind, kind, type, type, type, operate)
    {
    }

    private BoundBinaryOperator(TokenType syntaxKind, BoundBinaryOperatorKind kind, ValueType operandType, ValueType resultType, Func<object, object, object> operate)
        : this(syntaxKind, kind, operandType, operandType, resultType, operate)
    {
    }

    private BoundBinaryOperator(TokenType syntaxKind, BoundBinaryOperatorKind kind, ValueType leftType, ValueType rightType, ValueType resultType, Func<object, object, object> operate)
    {
        TypeKind = syntaxKind;
        Kind = kind;
        LeftType = leftType;
        RightType = rightType;
        Type = resultType;
        Operate = operate;
    }

    private static BoundBinaryOperator[] _operators = [
        new(TokenType.ADD,BoundBinaryOperatorKind.Addition, ValueType.Int, (a, b) => (int)a + (int)b),
        new(TokenType.SUB,BoundBinaryOperatorKind.Subtraction, ValueType.Int, (a, b) => (int)a - (int)b),
        new(TokenType.MUL,BoundBinaryOperatorKind.Multiplication, ValueType.Int, (a, b) => (int)a * (int)b),
        new(TokenType.DIV,BoundBinaryOperatorKind.Division, ValueType.Int, (a, b) => (int)a / (int)b),
        new(TokenType.MOD,BoundBinaryOperatorKind.Mod, ValueType.Int, (a, b) => (int)a % (int)b),
        new(TokenType.SlashI,BoundBinaryOperatorKind.RoundDiv, ValueType.Int, (a, b) => (int)Math.Round((double)a / (int)b)),

        new(TokenType.BitAnd,BoundBinaryOperatorKind.BitwiseAnd, ValueType.Int, (a, b) => (int)a & (int)b),
        new(TokenType.BitOr,BoundBinaryOperatorKind.BitwiseOr, ValueType.Int, (a, b) => (int)a | (int)b),
        new(TokenType.XOR,BoundBinaryOperatorKind.BitwiseXor, ValueType.Int, (a, b) => (int)a ^ (int)b),
        new(TokenType.SHL,BoundBinaryOperatorKind.BitLeftShift, ValueType.Int, (a, b) => (int)a << (int)b),
        new(TokenType.SHR,BoundBinaryOperatorKind.BitRightShift, ValueType.Int, (a, b) => (int)a >> (int)b),

        new(TokenType.EQL,BoundBinaryOperatorKind.Equals, ValueType.Int, ValueType.Bool, (v0, v1) => v0 == v1),
        new(TokenType.NEQ,BoundBinaryOperatorKind.NotEquals, ValueType.Int, ValueType.Bool, (v0, v1) => v0 != v1),
        new(TokenType.LESS,BoundBinaryOperatorKind.Less, ValueType.Int, ValueType.Bool, (v0, v1) => (int)v0 < (int)v1),
        new(TokenType.LEQ,BoundBinaryOperatorKind.LessOrEquals, ValueType.Int, ValueType.Bool, (v0, v1) => (int)v0 <= (int)v1),
        new(TokenType.GTR,BoundBinaryOperatorKind.Greater, ValueType.Int, ValueType.Bool, (v0, v1) => (int)v0 > (int)v1),
        new(TokenType.GEQ,BoundBinaryOperatorKind.GreaterOrEquals, ValueType.Int, ValueType.Bool, (v0, v1) => (int)v0 >= (int)v1),

        new(TokenType.EQL,BoundBinaryOperatorKind.Equals, ValueType.Bool, (v0, v1) => v0 == v1),
        new(TokenType.NEQ,BoundBinaryOperatorKind.NotEquals, ValueType.Bool, (v0, v1) => v0 != v1),

        new(TokenType.BitAnd,BoundBinaryOperatorKind.Addition, ValueType.String, (v0, v1) => $"{v0}{v1}"),
        new(TokenType.BitAnd,BoundBinaryOperatorKind.Addition, ValueType.Int,ValueType.String, ValueType.String, (v0, v1) => $"{v0}{v1}"),
        new(TokenType.BitAnd,BoundBinaryOperatorKind.Addition, ValueType.String,ValueType.Int, ValueType.String, (v0, v1) => $"{v0}{v1}"),
        ];
    public static BoundBinaryOperator? Bind(TokenType kind, ValueType leftType, ValueType rightType)
    {
        foreach (var op in _operators)
        {
            if (op.TypeKind == kind && op.LeftType == leftType && op.RightType == rightType)
                return op;
        }
        return null;
    }

    public static BoundBinaryOperator? Bind(string op)
    {
        return op switch
        {
            "+" => Bind(TokenType.ADD, ValueType.Int, ValueType.Int),
            "-" => Bind(TokenType.SUB, ValueType.Int, ValueType.Int),
            "*" => Bind(TokenType.MUL, ValueType.Int, ValueType.Int),
            "/" => Bind(TokenType.DIV, ValueType.Int, ValueType.Int),
            @"\" => Bind(TokenType.SlashI, ValueType.Int, ValueType.Int),

            "%" => Bind(TokenType.MOD, ValueType.Int, ValueType.Int),
            "&" => Bind(TokenType.BitAnd, ValueType.Int, ValueType.Int),
            "|" => Bind(TokenType.BitOr, ValueType.Int, ValueType.Int),
            "^" => Bind(TokenType.XOR, ValueType.Int, ValueType.Int),

            ">>" => Bind(TokenType.SlashI, ValueType.Int, ValueType.Int),
            "<<" => Bind(TokenType.SlashI, ValueType.Int, ValueType.Int),
            _ => null,
        };
    }
}

internal enum BoundBinaryOperatorKind
{
    Addition,
    Subtraction,
    Multiplication,
    Division,
    Mod,
    RoundDiv,
    LogicalAnd,
    LogicalOr,
    BitwiseAnd,
    BitwiseOr,
    BitwiseXor,
    BitLeftShift,
    BitRightShift,
    Equals,
    NotEquals,
    Less,
    LessOrEquals,
    Greater,
    GreaterOrEquals,
}