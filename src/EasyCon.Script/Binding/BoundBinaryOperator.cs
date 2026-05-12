using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;

namespace EasyCon.Script.Binding;

internal sealed class BoundBinaryOperator
{
    public TokenType TypeKind { get; }
    public BoundBinaryOperatorKind Kind { get; }
    public ScriptType LeftType { get; }
    public ScriptType RightType { get; }
    public ScriptType Type { get; }

    private BoundBinaryOperator(TokenType syntaxKind, BoundBinaryOperatorKind kind, ScriptType leftType, ScriptType rightType, ScriptType resultType)
    {
        TypeKind = syntaxKind;
        Kind = kind;
        LeftType = leftType;
        RightType = rightType;
        Type = resultType;
    }

    private static bool IsComparison(TokenType t) =>
        t is TokenType.EQL or TokenType.NEQ or TokenType.LESS or TokenType.LEQ or TokenType.GTR or TokenType.GEQ;

    private static bool IsArithmetic(TokenType t) =>
        t is TokenType.ADD or TokenType.SUB or TokenType.MUL or TokenType.DIV or TokenType.MOD or TokenType.SlashI;

    private static bool IsBitwise(TokenType t) =>
        t is TokenType.BitAnd or TokenType.BitOr or TokenType.XOR or TokenType.SHL or TokenType.SHR;

    private static bool IsLogical(TokenType t) =>
        t is TokenType.LogicAnd or TokenType.LogicOr;

    public static BoundBinaryOperator? Bind(TokenType kind, ScriptType leftType, ScriptType rightType)
    {
        if (kind.OperatorIsAug())
            kind = kind.GetBinaryOperatorOfAssignmentOperator();

        if (kind == TokenType.IN)
            return BindInOperator(kind, leftType, rightType);

        // 泛型数组拼接支持: Array<T> + Array<T>
        if (kind == TokenType.ADD && leftType is GenericType { Definition.Name: "Array" } && leftType.Equals(rightType))
            return new BoundBinaryOperator(kind, BoundBinaryOperatorKind.Addition, leftType, rightType, leftType);

        // 隐式转换
        var (convLeft, convRight) = ApplyImplicitConversion(kind, leftType, rightType);

        // 类型兼容性检查
        if (!IsTypeCompatible(kind, convLeft, convRight))
            return null;

        var resultType = GetResultType(kind, convLeft);
        if (resultType is null) return null;

        var opKind = GetOperatorKind(kind);
        return new BoundBinaryOperator(kind, opKind, convLeft, convRight, resultType);
    }

    private static BoundBinaryOperator? BindInOperator(TokenType kind, ScriptType leftType, ScriptType rightType)
    {
        if (rightType.Equals(ScriptType.String))
        {
            return leftType.Equals(ScriptType.String)
                ? new BoundBinaryOperator(kind, BoundBinaryOperatorKind.In, leftType, rightType, ScriptType.Bool)
                : null;
        }

        if (rightType is GenericType { Definition.Name: "Array" } arrayType &&
            arrayType.TypeArguments[0].Equals(leftType))
        {
            return new BoundBinaryOperator(kind, BoundBinaryOperatorKind.In, leftType, rightType, ScriptType.Bool);
        }

        return null;
    }

    private static (ScriptType Left, ScriptType Right) ApplyImplicitConversion(TokenType kind, ScriptType left, ScriptType right)
    {
        // String 拼接: & 和 + 与任意类型
        if ((kind == TokenType.BitAnd || kind == TokenType.ADD) &&
            (left.Equals(ScriptType.String) || right.Equals(ScriptType.String)))
            return (ScriptType.String, ScriptType.String);

        // int → double
        if (left.Equals(ScriptType.Int) && right.Equals(ScriptType.Double))
            left = ScriptType.Double;
        else if (right.Equals(ScriptType.Int) && left.Equals(ScriptType.Double))
            right = ScriptType.Double;

        // int/uint32 → uint64（所有运算符，优先于 uint32）
        if (left.Equals(ScriptType.Int) && right.Equals(ScriptType.UInt64))
            left = ScriptType.UInt64;
        else if (right.Equals(ScriptType.Int) && left.Equals(ScriptType.UInt64))
            right = ScriptType.UInt64;
        else if (left.Equals(ScriptType.UInt) && right.Equals(ScriptType.UInt64))
            left = ScriptType.UInt64;
        else if (right.Equals(ScriptType.UInt) && left.Equals(ScriptType.UInt64))
            right = ScriptType.UInt64;

        // int → uint32（所有运算符）
        if (left.Equals(ScriptType.Int) && right.Equals(ScriptType.UInt))
            left = ScriptType.UInt;
        else if (right.Equals(ScriptType.Int) && left.Equals(ScriptType.UInt))
            right = ScriptType.UInt;

        // int → byte（所有运算符）
        if (left.Equals(ScriptType.Int) && right.Equals(ScriptType.Byte))
            left = ScriptType.Byte;
        else if (right.Equals(ScriptType.Int) && left.Equals(ScriptType.Byte))
            right = ScriptType.Byte;

        // int → ptr（仅比较）
        if (IsComparison(kind))
        {
            if (left.Equals(ScriptType.Int) && right.Equals(ScriptType.Ptr))
                left = ScriptType.Ptr;
            else if (right.Equals(ScriptType.Int) && left.Equals(ScriptType.Ptr))
                right = ScriptType.Ptr;
        }

        return (left, right);
    }

    private static bool IsTypeCompatible(TokenType kind, ScriptType left, ScriptType right)
    {
        if (!left.Equals(right)) return false;

        if (IsLogical(kind)) return left.Equals(ScriptType.Bool);

        // STRING 拼接: & 和 +
        if ((kind == TokenType.ADD || kind == TokenType.BitAnd) && left.Equals(ScriptType.String))
            return true;

        if (IsBitwise(kind))
            return left.Equals(ScriptType.Int) || left.Equals(ScriptType.UInt) || left.Equals(ScriptType.UInt64) || left.Equals(ScriptType.Byte);

        if (kind == TokenType.MOD || kind == TokenType.SlashI)
            return left.Equals(ScriptType.Int) || left.Equals(ScriptType.UInt) || left.Equals(ScriptType.UInt64) || left.Equals(ScriptType.Byte);

        if (IsArithmetic(kind))
            return left.Equals(ScriptType.Int) || left.Equals(ScriptType.Double)
                || left.Equals(ScriptType.UInt) || left.Equals(ScriptType.UInt64) || left.Equals(ScriptType.Byte);

        if (IsComparison(kind))
            return left.Equals(ScriptType.Int) || left.Equals(ScriptType.Double) ||
                   left.Equals(ScriptType.Ptr) || left.Equals(ScriptType.Bool) ||
                   left.Equals(ScriptType.String) || left.Equals(ScriptType.UInt) ||
                   left.Equals(ScriptType.UInt64) || left.Equals(ScriptType.Byte);

        return false;
    }

    private static ScriptType? GetResultType(TokenType kind, ScriptType operandType)
    {
        if (IsComparison(kind)) return ScriptType.Bool;
        if (IsLogical(kind)) return ScriptType.Bool;
        return operandType;
    }

    private static BoundBinaryOperatorKind GetOperatorKind(TokenType kind) => kind switch
    {
        TokenType.ADD => BoundBinaryOperatorKind.Addition,
        TokenType.SUB => BoundBinaryOperatorKind.Subtraction,
        TokenType.MUL => BoundBinaryOperatorKind.Multiplication,
        TokenType.DIV => BoundBinaryOperatorKind.Division,
        TokenType.MOD => BoundBinaryOperatorKind.Mod,
        TokenType.SlashI => BoundBinaryOperatorKind.RoundDiv,
        TokenType.EQL => BoundBinaryOperatorKind.Equals,
        TokenType.NEQ => BoundBinaryOperatorKind.NotEquals,
        TokenType.LESS => BoundBinaryOperatorKind.Less,
        TokenType.LEQ => BoundBinaryOperatorKind.LessOrEquals,
        TokenType.GTR => BoundBinaryOperatorKind.Greater,
        TokenType.GEQ => BoundBinaryOperatorKind.GreaterOrEquals,
        TokenType.IN => BoundBinaryOperatorKind.In,
        TokenType.BitAnd => BoundBinaryOperatorKind.BitwiseAnd,
        TokenType.BitOr => BoundBinaryOperatorKind.BitwiseOr,
        TokenType.XOR => BoundBinaryOperatorKind.BitwiseXor,
        TokenType.SHL => BoundBinaryOperatorKind.BitLeftShift,
        TokenType.SHR => BoundBinaryOperatorKind.BitRightShift,
        TokenType.LogicAnd => BoundBinaryOperatorKind.LogicalAnd,
        TokenType.LogicOr => BoundBinaryOperatorKind.LogicalOr,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
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
    In,
}