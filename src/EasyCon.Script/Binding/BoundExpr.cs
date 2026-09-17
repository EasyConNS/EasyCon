using EasyCon.Script.Runtime;
using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using System.Collections.Immutable;

namespace EasyCon.Script.Binding;

internal abstract class BoundExpr(AstNode expr) : BoundNode
{
    public AstNode Syntax = expr;
    public abstract ScriptType Type { get; }
    public object? ConstantValue = null;
}

internal sealed class BoundErrorExpression(AstNode expr) : BoundExpr(expr)
{
    public override ScriptType Type => throw new NotImplementedException();

    public override BoundNodeKind Kind => throw new NotImplementedException();
}


internal sealed class BoundLiteralExpression : BoundExpr
{
    public readonly ScriptType LiteralType;
    public override ScriptType Type => LiteralType;
    public override BoundNodeKind Kind => BoundNodeKind.Literal;
    public BoundLiteralExpression(AstNode syntax, object? value, ScriptType type) : base(syntax)
    {
        LiteralType = type;
        ConstantValue = value;
    }
}

internal sealed class BoundVariableExpression : BoundExpr
{
    public readonly VariableSymbol Variable;
    public override ScriptType Type { get; }
    public override BoundNodeKind Kind => BoundNodeKind.Variable;
    public BoundVariableExpression(AstNode syntax, VariableSymbol variable) : base(syntax)
    {
        Variable = variable;
        Type = variable.Type;
        ConstantValue = variable.Value;
    }
}

internal sealed class BoundIndexVariableExpression(AstNode syntax, BoundExpr baseExpr, BoundExpr idx, ScriptType elementType) : BoundExpr(syntax)
{
    public override ScriptType Type { get; } = elementType;
    public override BoundNodeKind Kind => BoundNodeKind.IndexVariable;
    public readonly BoundExpr BaseExpression = baseExpr;
    public readonly BoundExpr Index = idx;
}

internal sealed class BoundSliceExpression(AstNode syntax, BoundExpr baseExpr, BoundExpr st, BoundExpr end, ScriptType resultType) : BoundExpr(syntax)
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

    public BoundIndexDeclxpression(AstNode syntax, ImmutableArray<BoundExpr> items, ScriptType? annotatedElementType = null) : base(syntax)
    {
        Items = items;
        // 优先使用类型标注，其次从元素推断，最后默认 int
        var elementType = annotatedElementType
            ?? items.Select(i => i.Type).FirstOrDefault(ScriptType.Int);
        Type = ScriptType.ArrayOf(elementType);
    }
}

internal sealed class BoundRuntimeValueExpression(AstNode syntax, string name, ScriptType type) : BoundExpr(syntax)
{
    public readonly string Name = name;
    public override ScriptType Type { get; } = type;
    public override BoundNodeKind Kind => BoundNodeKind.RuntimeValue;
}

internal sealed class BoundImageLabelExpression(AstNode syntax, string name) : BoundExpr(syntax)
{
    public readonly string Name = name;
    public override ScriptType Type => ScriptType.Int;
    public override BoundNodeKind Kind => BoundNodeKind.ExLabelVariable;
}

internal sealed class BoundUnaryExpression(AstNode syntax, BoundUnaryOperator op, BoundExpr operand) : BoundExpr(syntax)
{
    public readonly BoundExpr Operand = operand;
    public readonly BoundUnaryOperator Op = op;

    public override ScriptType Type => Op.Type;
    public override BoundNodeKind Kind => BoundNodeKind.UnaryExpression;
}

internal sealed class BoundBinaryExpression(AstNode syntax, BoundExpr left, BoundBinaryOperator op, BoundExpr right) : BoundExpr(syntax)
{
    public readonly BoundExpr Left = left;
    public readonly BoundExpr Right = right;
    public readonly BoundBinaryOperator Op = op;

    public override ScriptType Type => Op.Type;
    public override BoundNodeKind Kind => BoundNodeKind.BinaryExpression;

}

internal sealed class BoundConversionExpression(AstNode syntax, ScriptType type, BoundExpr expr) : BoundExpr(syntax)
{
    public override ScriptType Type => type;

    public override BoundNodeKind Kind => BoundNodeKind.ConversionExpression;
    public BoundExpr Expression = expr;
}

internal sealed class BoundCallExpression(AstNode syntax, FunctionSymbol function, ImmutableArray<BoundExpr> arguments, ScriptType instantiatedType) : BoundExpr(syntax)
{
    public override ScriptType Type { get; } = instantiatedType;
    public readonly FunctionSymbol Function = function;
    public readonly ImmutableArray<BoundExpr> Arguments = arguments;
    public override BoundNodeKind Kind => BoundNodeKind.CallExpression;
}

internal sealed class BoundStructInitExpression(AstNode syntax, EcsStructDef def) : BoundExpr(syntax)
{
    public override ScriptType Type { get; } = new StructType(def);
    public readonly EcsStructDef Definition = def;
    public override BoundNodeKind Kind => BoundNodeKind.StructInit;
}

internal sealed class BoundFieldAccessExpression(AstNode syntax, BoundExpr target, EcsFieldDef field, ScriptType resultType) : BoundExpr(syntax)
{
    public override ScriptType Type { get; } = resultType;
    public readonly BoundExpr Target = target;
    public readonly EcsFieldDef Field = field;
    public override BoundNodeKind Kind => BoundNodeKind.FieldAccess;
}