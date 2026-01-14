using EasyCon.Script2.Syntax;

namespace EasyCon.Script.Binding;

internal sealed class BoundUnaryOperator
{
    public TokenType TypeKind { get; }
    public BoundUnaryOperatorKind Kind { get; }
    public ValueType Type { get; }
}
internal enum BoundUnaryOperatorKind
{
    Subtraction,
    BitwiseNot,
}