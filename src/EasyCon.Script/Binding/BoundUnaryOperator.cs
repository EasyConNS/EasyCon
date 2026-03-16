using EasyCon.Script2.Syntax;

namespace EasyCon.Script.Binding;

internal sealed class BoundUnaryOperator
{
    public TokenType TypeKind { get; }
    public BoundUnaryOperatorKind Kind { get; }
    public ValueType Type { get; }
    public readonly Func<object, object> Operate;

    private BoundUnaryOperator(TokenType syntaxKind, BoundUnaryOperatorKind kind, ValueType resultType, Func<object, object> operate)
    {
        TypeKind = syntaxKind;
        Kind = kind;
        Type = resultType;
        Operate = operate;
    }

    private static BoundUnaryOperator[] _operators =
    {
        new(TokenType.SUB, BoundUnaryOperatorKind.Subtraction, ValueType.Int, a=> -(int)a),
        new(TokenType.BitNot, BoundUnaryOperatorKind.BitwiseNot, ValueType.Int, a=> ~(int)a),
        new(TokenType.LogicNot, BoundUnaryOperatorKind.LogicNot, ValueType.Bool, a=> !(bool)a),

    };

    public static BoundUnaryOperator? Bind(TokenType kind, ValueType operandType)
    {
        foreach (var op in _operators)
        {
            if (op.TypeKind == kind && op.Type == operandType)
                return op;
        }
        return null;
    }
}
internal enum BoundUnaryOperatorKind
{
    Subtraction,
    BitwiseNot,
    LogicNot,
}