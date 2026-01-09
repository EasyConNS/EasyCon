using EasyCon.Script.Parsing;

namespace EasyCon.Script.Binding;

internal abstract class BoundExpr(ExprBase expr)
{
    public ExprBase Syntax = expr;
    public abstract ValueType Type { get; }
    public virtual object? ConstantValue => null;
}

internal sealed class BoundLiteralExpression : BoundExpr
{
    public override ValueType Type { get; }
    public override object ConstantValue { get; }
    public BoundLiteralExpression(ExprBase syntax, object value) : base( syntax)
    {
        if (value is bool)
            Type = ValueType.Bool;
        else if (value is int)
            Type = ValueType.Int;
        else if (value is string)
            Type = ValueType.String;
        else
            throw new Exception($"Unexpected literal '{value}' of type {value.GetType()}");

        ConstantValue = value;
    }
}

internal sealed class BoundVariableExpression(ExprBase syntax, VariableSymbol variable) : BoundExpr(syntax)
{
    public readonly VariableSymbol Variable = variable;
    public override ValueType Type => Variable.Type;
}

internal sealed class BoundBinaryExpression(ExprBase syntax, BoundExpr left, BoundBinaryOperator op, BoundExpr right) : BoundExpr(syntax)
{
    public readonly BoundExpr Left = left;
    public readonly BoundExpr Right = right;
    public readonly BoundBinaryOperator Op = op;

    public override ValueType Type => Op.Type;
}