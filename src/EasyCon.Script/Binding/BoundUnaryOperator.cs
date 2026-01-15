using EasyCon.Script2.Syntax;

namespace EasyCon.Script.Binding;

internal sealed class BoundUnaryOperator
{
    public TokenType TypeKind { get; }
    public BoundUnaryOperatorKind Kind { get; }
    public ValueType Type { get; }

    private BoundUnaryOperator(TokenType syntaxKind, BoundUnaryOperatorKind kind, ValueType resultType)
    {
        TypeKind = syntaxKind;
        Kind = kind;
        Type = resultType;
    }

    private static BoundUnaryOperator[] _operators =
    {
        new(TokenType.SUB, BoundUnaryOperatorKind.Subtraction, ValueType.Int),
        new(TokenType.BitNot, BoundUnaryOperatorKind.BitwiseNot, ValueType.Int),

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
}