using EasyCon.Script2.Syntax;

namespace EasyCon.Script.Binding;

internal sealed class BoundBinaryOperator
{
    public TokenType TypeKind { get; }
    public BoundBinaryOperatorKind Kind { get; }
    public ValueType LeftType { get; }
    public ValueType RightType { get; }
    public ValueType Type { get; }

    private BoundBinaryOperator(TokenType syntaxKind, BoundBinaryOperatorKind kind, ValueType type)
    : this(syntaxKind, kind, type, type, type)
    {
    }

    private BoundBinaryOperator(TokenType syntaxKind, BoundBinaryOperatorKind kind, ValueType operandType, ValueType resultType)
        : this(syntaxKind, kind, operandType, operandType, resultType)
    {
    }

    private BoundBinaryOperator(TokenType syntaxKind, BoundBinaryOperatorKind kind, ValueType leftType, ValueType rightType, ValueType resultType)
    {
        TypeKind = syntaxKind;
        Kind = kind;
        LeftType = leftType;
        RightType = rightType;
        Type = resultType;
    }

    private static BoundBinaryOperator[] _operators = [
        new(TokenType.ADD,BoundBinaryOperatorKind.Addition, ValueType.Int),
        new(TokenType.SUB,BoundBinaryOperatorKind.Subtraction, ValueType.Int),
        new(TokenType.MUL,BoundBinaryOperatorKind.Multiplication, ValueType.Int),
        new(TokenType.DIV,BoundBinaryOperatorKind.Division, ValueType.Int),
        new(TokenType.MOD,BoundBinaryOperatorKind.Mod, ValueType.Int),
        new(TokenType.SlashI,BoundBinaryOperatorKind.RoundDiv, ValueType.Int),

        new(TokenType.BitAnd,BoundBinaryOperatorKind.BitwiseAnd, ValueType.Int),
        new(TokenType.BitOr,BoundBinaryOperatorKind.BitwiseOr, ValueType.Int),
        new(TokenType.XOR,BoundBinaryOperatorKind.BitwiseXor, ValueType.Int),
        new(TokenType.SHL,BoundBinaryOperatorKind.BitLeftShift, ValueType.Int),
        new(TokenType.SHR,BoundBinaryOperatorKind.BitRightShift, ValueType.Int),

        new(TokenType.EQL,BoundBinaryOperatorKind.Equals, ValueType.Int, ValueType.Bool),
        new(TokenType.NEQ,BoundBinaryOperatorKind.NotEquals, ValueType.Int, ValueType.Bool),
        new(TokenType.LESS,BoundBinaryOperatorKind.Less, ValueType.Int, ValueType.Bool),
        new(TokenType.LEQ,BoundBinaryOperatorKind.LessOrEquals, ValueType.Int, ValueType.Bool),
        new(TokenType.GTR,BoundBinaryOperatorKind.Greater, ValueType.Int, ValueType.Bool),
        new(TokenType.GEQ,BoundBinaryOperatorKind.GreaterOrEquals, ValueType.Int, ValueType.Bool),

        new(TokenType.EQL,BoundBinaryOperatorKind.Equals, ValueType.Bool),
        new(TokenType.NEQ,BoundBinaryOperatorKind.NotEquals, ValueType.Bool),

        new(TokenType.ADD,BoundBinaryOperatorKind.Addition, ValueType.String),
        new(TokenType.BitAnd,BoundBinaryOperatorKind.BitwiseAnd, ValueType.Int,ValueType.String, ValueType.String),
        new(TokenType.BitAnd,BoundBinaryOperatorKind.BitwiseAnd, ValueType.String,ValueType.Int, ValueType.String),
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