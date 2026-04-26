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

    public readonly Func<Value, Value, Value> Operate;

    private BoundBinaryOperator(TokenType syntaxKind, BoundBinaryOperatorKind kind, ScriptType type, Func<Value, Value, Value> operate)
    : this(syntaxKind, kind, type, type, type, operate)
    {
    }

    private BoundBinaryOperator(TokenType syntaxKind, BoundBinaryOperatorKind kind, ScriptType operandType, ScriptType resultType, Func<Value, Value, Value> operate)
        : this(syntaxKind, kind, operandType, operandType, resultType, operate)
    {
    }

    private BoundBinaryOperator(TokenType syntaxKind, BoundBinaryOperatorKind kind, ScriptType leftType, ScriptType rightType, ScriptType resultType, Func<Value, Value, Value> operate)
    {
        TypeKind = syntaxKind;
        Kind = kind;
        LeftType = leftType;
        RightType = rightType;
        Type = resultType;
        Operate = operate;
    }

    private static BoundBinaryOperator[] _operators = [
        new(TokenType.ADD,BoundBinaryOperatorKind.Addition, ScriptType.Int, (a, b) => a.AsInt()+ b.AsInt()),
        new(TokenType.SUB,BoundBinaryOperatorKind.Subtraction, ScriptType.Int, (a, b) => a.AsInt() - b.AsInt()),
        new(TokenType.MUL,BoundBinaryOperatorKind.Multiplication, ScriptType.Int, (a, b) => a.AsInt() * b.AsInt()),
        new(TokenType.DIV,BoundBinaryOperatorKind.Division, ScriptType.Int, (a, b) => a.AsInt() / b.AsInt()),
        new(TokenType.MOD,BoundBinaryOperatorKind.Mod, ScriptType.Int, (a, b) => a.AsInt() % b.AsInt()),
        new(TokenType.SlashI,BoundBinaryOperatorKind.RoundDiv, ScriptType.Int, (a, b) => (int)Math.Round((double)a.AsInt() / b.AsInt(), MidpointRounding.AwayFromZero)),

        new(TokenType.ADD,BoundBinaryOperatorKind.Addition, ScriptType.Double, (a, b) => a.AsDouble() + b.AsDouble()),
        new(TokenType.SUB,BoundBinaryOperatorKind.Subtraction, ScriptType.Double, (a, b) => a.AsDouble() - b.AsDouble()),
        new(TokenType.MUL,BoundBinaryOperatorKind.Multiplication, ScriptType.Double, (a, b) => a.AsDouble() * b.AsDouble()),
        new(TokenType.DIV,BoundBinaryOperatorKind.Division, ScriptType.Double, (a, b) => a.AsDouble() / b.AsDouble()),

        new(TokenType.BitAnd,BoundBinaryOperatorKind.BitwiseAnd, ScriptType.Int, (a, b) => a.AsInt() & b.AsInt()),
        new(TokenType.BitOr,BoundBinaryOperatorKind.BitwiseOr, ScriptType.Int, (a, b) => a.AsInt() | b.AsInt()),
        new(TokenType.XOR,BoundBinaryOperatorKind.BitwiseXor, ScriptType.Int, (a, b) => a.AsInt() ^ b.AsInt()),
        new(TokenType.SHL,BoundBinaryOperatorKind.BitLeftShift, ScriptType.Int, (a, b) => a.AsInt() << b.AsInt()),
        new(TokenType.SHR,BoundBinaryOperatorKind.BitRightShift, ScriptType.Int, (a, b) => a.AsInt() >> b.AsInt()),

        new(TokenType.EQL,BoundBinaryOperatorKind.Equals, ScriptType.Int, ScriptType.Bool, (v0, v1) => v0.AsInt() == v1.AsInt()),
        new(TokenType.NEQ,BoundBinaryOperatorKind.NotEquals, ScriptType.Int, ScriptType.Bool, (v0, v1) => v0.AsInt() != v1.AsInt()),
        new(TokenType.LESS,BoundBinaryOperatorKind.Less, ScriptType.Int, ScriptType.Bool, (v0, v1) => v0.AsInt() < v1.AsInt()),
        new(TokenType.LEQ,BoundBinaryOperatorKind.LessOrEquals, ScriptType.Int, ScriptType.Bool, (v0, v1) => v0.AsInt() <= v1.AsInt()),
        new(TokenType.GTR,BoundBinaryOperatorKind.Greater, ScriptType.Int, ScriptType.Bool, (v0, v1) => v0.AsInt() > v1.AsInt()),
        new(TokenType.GEQ,BoundBinaryOperatorKind.GreaterOrEquals, ScriptType.Int, ScriptType.Bool, (v0, v1) => v0.AsInt() >= v1.AsInt()),

        new(TokenType.EQL,BoundBinaryOperatorKind.Equals, ScriptType.Bool, (v0, v1) => Equals(v0, v1)),
        new(TokenType.NEQ,BoundBinaryOperatorKind.NotEquals, ScriptType.Bool, (v0, v1) => !Equals(v0, v1)),
        new(TokenType.EQL,BoundBinaryOperatorKind.Equals, ScriptType.String, ScriptType.Bool, (v0, v1) => Equals(v0, v1)),
        new(TokenType.NEQ,BoundBinaryOperatorKind.NotEquals, ScriptType.String, ScriptType.Bool, (v0, v1) => !Equals(v0, v1)),
        new(TokenType.LogicAnd, BoundBinaryOperatorKind.LogicalAnd,ScriptType.Bool, (v0, v1) => {
            if(!v0.AsBool()) { return false; } return v1.AsBool();
        }),
        new(TokenType.LogicOr, BoundBinaryOperatorKind.LogicalOr,ScriptType.Bool, (v0, v1) => {
            if(v0.AsBool()) { return true; } return v1.AsBool();
        }),

        new(TokenType.ADD,BoundBinaryOperatorKind.Addition, ScriptType.String, (v0, v1) => $"{v0}{v1}"),
        new(TokenType.BitAnd,BoundBinaryOperatorKind.Addition, ScriptType.String, (v0, v1) => $"{v0}{v1}"),
        new(TokenType.BitAnd,BoundBinaryOperatorKind.Addition, ScriptType.Int,ScriptType.String, ScriptType.String, (v0, v1) => $"{v0}{v1}"),
        new(TokenType.BitAnd,BoundBinaryOperatorKind.Addition, ScriptType.String,ScriptType.Int, ScriptType.String, (v0, v1) => $"{v0}{v1}"),
        new(TokenType.BitAnd,BoundBinaryOperatorKind.Addition, ScriptType.Bool,ScriptType.String, ScriptType.String, (v0, v1) => $"{v0}{v1}"),
        new(TokenType.BitAnd,BoundBinaryOperatorKind.Addition, ScriptType.String,ScriptType.Bool, ScriptType.String, (v0, v1) => $"{v0}{v1}"),
        ];
    public static BoundBinaryOperator? Bind(TokenType kind, ScriptType leftType, ScriptType rightType)
    {
        if (kind.OperatorIsAug())
            kind = kind.GetBinaryOperatorOfAssignmentOperator();
        foreach (var op in _operators)
        {
            if (op.TypeKind == kind && op.LeftType == leftType && op.RightType == rightType)
                return op;
        }
        // 泛型数组拼接支持: Array<T> + Array<T>
        if (kind == TokenType.ADD && leftType is GenericType { Definition.Name: "Array" } && leftType.Equals(rightType))
        {
            return new BoundBinaryOperator(kind, BoundBinaryOperatorKind.Addition, leftType, leftType, leftType, (a, b) => a.Concat(b));
        }
        return null;
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