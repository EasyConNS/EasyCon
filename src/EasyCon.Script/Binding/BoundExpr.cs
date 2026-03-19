using EasyCon.Script.Parsing;
using EasyCon.Script2.Binding;
using System.Collections.Immutable;

namespace EasyCon.Script.Binding;

internal abstract class BoundExpr(ExprBase expr) : BoundNode
{
    public ExprBase Syntax = expr;
    public abstract ValueType Type { get; }
    public object? ConstantValue = null;
}

internal sealed class BoundLiteralExpression : BoundExpr
{
    public override ValueType Type { get; }
    public override BoundNodeKind Kind => BoundNodeKind.Literal;
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

internal sealed class BoundVariableExpression : BoundExpr
{
    public readonly VariableSymbol Variable;
    public override ValueType Type { get; }
    public override BoundNodeKind Kind => BoundNodeKind.Variable;
    public BoundVariableExpression(ExprBase syntax, VariableSymbol variable) : base(syntax)
    {
        Variable = variable;
        Type = variable.Type;
        ConstantValue = variable.Value;
    }
}

internal sealed class BoundExternalVariableExpression(ExtVarExpr syntax, ExternalVariable operand) : BoundExpr(syntax)
{
    public readonly ExternalVariable Label = operand;

    public override ValueType Type => ValueType.Int;
    public override BoundNodeKind Kind => BoundNodeKind.ExLabelVariable;
}

internal sealed class BoundUnaryExpression(ExprBase syntax, BoundUnaryOperator op, BoundExpr operand) : BoundExpr(syntax)
{
    public readonly BoundExpr Operand = operand;
    public readonly BoundUnaryOperator Op = op;

    public override ValueType Type => Op.Type;
    public override BoundNodeKind Kind => BoundNodeKind.UnaryExpression;
}

internal sealed class BoundBinaryExpression(ExprBase syntax, BoundExpr left, BoundBinaryOperator op, BoundExpr right) : BoundExpr(syntax)
{
    public readonly BoundExpr Left = left;
    public readonly BoundExpr Right = right;
    public readonly BoundBinaryOperator Op = op;

    public override ValueType Type => Op.Type;
    public override BoundNodeKind Kind => BoundNodeKind.BinaryExpression;
}

internal sealed class BoundConversionExpression(ExprBase syntax, ValueType type, BoundExpr expr) : BoundExpr(syntax)
{
    public override ValueType Type => type;

    public override BoundNodeKind Kind => BoundNodeKind.ConversionExpression;
    public BoundExpr Expression = expr;
}

internal sealed class BoundAssignExpression(ExprBase syntax, VariableSymbol variable, BoundExpr expr) :BoundExpr(syntax)
{
    public override ValueType Type => Expression.Type;
    public readonly VariableSymbol Variable = variable;
    public readonly BoundExpr Expression = expr;
    public override BoundNodeKind Kind => BoundNodeKind.AssignmentExpression;
}

internal sealed class BoundCallExpression(ExprBase syntax, FunctionSymbol function, ImmutableArray<BoundExpr> arguments) : BoundExpr(syntax)
{
    public override ValueType Type => Function.Type;
    public FunctionSymbol Function = function;
    public ImmutableArray<BoundExpr> Arguments = arguments;
    public override BoundNodeKind Kind => BoundNodeKind.CallExpression;
}