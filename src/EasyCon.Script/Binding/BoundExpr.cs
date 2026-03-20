using EasyCon.Script.Parsing;
using EasyCon.Script2.Binding;
using System.Collections.Immutable;

namespace EasyCon.Script.Binding;

internal abstract class BoundExpr(ExprBase expr) : BoundNode
{
    public ExprBase Syntax = expr;
    public abstract ValueType Type { get; }
    public Value ConstantValue = Value.Void;
}

internal sealed class BoundLiteralExpression : BoundExpr
{
    public override ValueType Type { get; }
    public override BoundNodeKind Kind => BoundNodeKind.Literal;
    public BoundLiteralExpression(ExprBase syntax, Value value) : base( syntax)
    {
        Type = value.Kind;
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
        ConstantValue = Value.From(variable.Value);
    }
}

internal sealed class BoundIndexVariableExpression(ExprBase syntax, BoundExpr variable, BoundExpr idx) : BoundExpr(syntax)
{
    public override ValueType Type => ValueType.Any;

    public override BoundNodeKind Kind => BoundNodeKind.IndexVariable;
    public readonly BoundExpr Variable = variable;
    public readonly BoundExpr Index = idx;

}

internal sealed class BoundIndexDeclxpression(ExprBase syntax, ImmutableArray<BoundExpr> idx) : BoundExpr(syntax)
{
    public override ValueType Type => ValueType.Array;
    public ImmutableArray<BoundExpr> Items = idx;
    public override BoundNodeKind Kind => BoundNodeKind.IndexDecl;
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