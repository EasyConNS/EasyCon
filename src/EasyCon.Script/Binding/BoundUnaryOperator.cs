using EasyCon.Script2.Syntax;

namespace EasyCon.Script.Binding;

internal sealed class BoundUnaryOperator
{
    public TokenType TypeKind { get; }
    public BoundUnaryOperatorKind Kind { get; }
    public ValueType Type { get; }

    public static BoundUnaryOperator? Bind(TokenType kind, ValueType operandType)
    {
        return null;
    }
}
internal enum BoundUnaryOperatorKind
{
    Subtraction,
    BitwiseNot,
}