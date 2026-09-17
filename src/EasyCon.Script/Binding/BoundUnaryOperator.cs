using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;

namespace EasyCon.Script.Binding;

internal sealed class BoundUnaryOperator
{
    public TokenType TypeKind { get; }
    public BoundUnaryOperatorKind Kind { get; }
    public ScriptType Type { get; }
    public readonly Func<Value, Value> Operate;

    private BoundUnaryOperator(TokenType syntaxKind, BoundUnaryOperatorKind kind, ScriptType resultType, Func<Value, Value> operate)
    {
        TypeKind = syntaxKind;
        Kind = kind;
        Type = resultType;
        Operate = operate;
    }

    private static BoundUnaryOperator[] _operators =
    {
        new(TokenType.SUB, BoundUnaryOperatorKind.Subtraction, ScriptType.Int, a => -a.AsInt()),
        new(TokenType.SUB, BoundUnaryOperatorKind.Subtraction, ScriptType.Byte, a => Value.FromByte((byte)(-a.AsByte()))),
        new(TokenType.SUB, BoundUnaryOperatorKind.Subtraction, ScriptType.UInt, a => Value.FromUInt(unchecked((uint)(-(int)a.AsUInt())))),
        new(TokenType.BitNot, BoundUnaryOperatorKind.BitwiseNot, ScriptType.Int, a => ~a.AsInt()),
        new(TokenType.BitNot, BoundUnaryOperatorKind.BitwiseNot, ScriptType.Byte, a => Value.FromByte((byte)(~a.AsByte()))),
        new(TokenType.BitNot, BoundUnaryOperatorKind.BitwiseNot, ScriptType.UInt, a => Value.FromUInt(~a.AsUInt())),
        new(TokenType.SUB, BoundUnaryOperatorKind.Subtraction, ScriptType.UInt64, a => Value.FromUInt64(unchecked((ulong)(-(long)a.AsUInt64())))),
        new(TokenType.BitNot, BoundUnaryOperatorKind.BitwiseNot, ScriptType.UInt64, a => Value.FromUInt64(~a.AsUInt64())),
        new(TokenType.LogicNot, BoundUnaryOperatorKind.LogicNot, ScriptType.Bool, a => !a.AsBool()),
    };

    public static BoundUnaryOperator? Bind(TokenType kind, ScriptType operandType)
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