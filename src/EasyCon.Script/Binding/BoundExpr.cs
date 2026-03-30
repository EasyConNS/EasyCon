using EasyCon.Script.Parsing;
using EasyCon.Script2.Binding;
using System.Collections.Immutable;

namespace EasyCon.Script.Binding;

internal abstract class BoundExpr(ExprBase expr) : BoundNode
{
    public ExprBase Syntax = expr;
    public abstract ScriptType Type { get; }
    public Value ConstantValue = Value.Void;

    public List<string> GetReferencedVariables()
    {
        var variables = new List<string>();
        CollectVariables(this, variables);
        return variables;
    }
    private void CollectVariables(BoundNode node, List<string> variables)
    {
        if (node == null) return;
        if (node is BoundVariableExpression varNode)
        {
            variables.Add(varNode.Variable.Name);
        }
        else if (node is BoundIndexVariableExpression idxVarNode)
        {
            // TODO
        }
        else if (node is BoundUnaryExpression unaryNode)
        {
            CollectVariables(unaryNode.Operand, variables);
        }
        else if (node is BoundBinaryExpression binOpNode)
        {
            CollectVariables(binOpNode.Left, variables);
            CollectVariables(binOpNode.Right, variables);
        }
        else if (node is BoundConversionExpression convNode)
        {
            CollectVariables(convNode.Expression, variables);
        }
        else if (node is BoundCallExpression callNode)
        {
            foreach (var arg in callNode.Arguments)
            {
                CollectVariables(arg, variables);
            }
        }
        else if (node is BoundAssignExpression assignNode)
        {
            CollectVariables(assignNode.Expression, variables);
        }
    }
}

internal sealed class BoundLiteralExpression : BoundExpr
{
    public override ScriptType Type { get; }
    public override BoundNodeKind Kind => BoundNodeKind.Literal;
    public BoundLiteralExpression(ExprBase syntax, Value value) : base( syntax)
    {
        Type = value.Type;
        ConstantValue = value;
    }
}

internal sealed class BoundVariableExpression : BoundExpr
{
    public readonly VariableSymbol Variable;
    public override ScriptType Type { get; }
    public override BoundNodeKind Kind => BoundNodeKind.Variable;
    public BoundVariableExpression(ExprBase syntax, VariableSymbol variable) : base(syntax)
    {
        Variable = variable;
        Type = variable.Type;
        ConstantValue = Value.From(variable.Value);
    }
}

internal sealed class BoundIndexVariableExpression(ExprBase syntax, BoundExpr baseExpr, BoundExpr idx, ScriptType elementType) : BoundExpr(syntax)
{
    public override ScriptType Type { get; } = elementType;
    public override BoundNodeKind Kind => BoundNodeKind.IndexVariable;
    public readonly BoundExpr BaseExpression = baseExpr;
    public readonly BoundExpr Index = idx;
}

internal sealed class BoundSliceExpression(ExprBase syntax, BoundExpr baseExpr, BoundExpr st, BoundExpr end, ScriptType resultType) : BoundExpr(syntax)
{
    public override ScriptType Type { get; } = resultType;
    public override BoundNodeKind Kind => BoundNodeKind.SliceVariable;
    public readonly BoundExpr BaseExpression = baseExpr;
    public readonly BoundExpr Start = st;
    public readonly BoundExpr End = end;
}

internal sealed class BoundIndexDeclxpression : BoundExpr
{
    public override ScriptType Type { get; }
    public readonly ImmutableArray<BoundExpr> Items;
    public override BoundNodeKind Kind => BoundNodeKind.IndexDecl;

    public BoundIndexDeclxpression(ExprBase syntax, ImmutableArray<BoundExpr> items) : base(syntax)
    {
        Items = items;
        // 如果数组为空，默认可以是 Array<any> 或者报错，这里假设至少有一个元素
        var elementType = items.Length > 0 ? items[0].Type : ScriptType.Int;
        Type = ScriptType.ArrayDefinition.Bind(elementType);
    }
}

internal sealed class BoundExternalVariableExpression(ExtVarExpr syntax, ExternalVariable operand) : BoundExpr(syntax)
{
    public readonly ExternalVariable Label = operand;

    public override ScriptType Type => ScriptType.Int;
    public override BoundNodeKind Kind => BoundNodeKind.ExLabelVariable;
}

internal sealed class BoundUnaryExpression(ExprBase syntax, BoundUnaryOperator op, BoundExpr operand) : BoundExpr(syntax)
{
    public readonly BoundExpr Operand = operand;
    public readonly BoundUnaryOperator Op = op;

    public override ScriptType Type => Op.Type;
    public override BoundNodeKind Kind => BoundNodeKind.UnaryExpression;
}

internal sealed class BoundBinaryExpression(ExprBase syntax, BoundExpr left, BoundBinaryOperator op, BoundExpr right) : BoundExpr(syntax)
{
    public readonly BoundExpr Left = left;
    public readonly BoundExpr Right = right;
    public readonly BoundBinaryOperator Op = op;

    public override ScriptType Type => Op.Type;
    public override BoundNodeKind Kind => BoundNodeKind.BinaryExpression;
}

internal sealed class BoundConversionExpression(ExprBase syntax, ScriptType type, BoundExpr expr) : BoundExpr(syntax)
{
    public override ScriptType Type => type;

    public override BoundNodeKind Kind => BoundNodeKind.ConversionExpression;
    public BoundExpr Expression = expr;
}

internal sealed class BoundAssignExpression(ExprBase syntax, VariableSymbol variable, BoundExpr expr) :BoundExpr(syntax)
{
    public override ScriptType Type => Expression.Type;
    public readonly VariableSymbol Variable = variable;
    public readonly BoundExpr Expression = expr;
    public override BoundNodeKind Kind => BoundNodeKind.AssignmentExpression;
}

internal sealed class BoundCallExpression(ExprBase syntax, FunctionSymbol function, ImmutableArray<BoundExpr> arguments, ScriptType instantiatedType) : BoundExpr(syntax)
{
    public override ScriptType Type { get; } = instantiatedType;
    public readonly FunctionSymbol Function = function;
    public readonly ImmutableArray<BoundExpr> Arguments = arguments;
    public override BoundNodeKind Kind => BoundNodeKind.CallExpression;
}