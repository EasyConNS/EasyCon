using System.Collections.Immutable;

namespace EasyCon.Script.Syntax;

// base of valuetype
abstract class BaseExpr(Token syntax) : AstNode(syntax)
{
    public abstract string GetCodeText();
}

// 左值表达式基类：可出现在 = 左侧的类型
abstract class TargetExpr(Token syntax) : BaseExpr(syntax);

// 字面量表达式
sealed class LiteralExpr(Token keyword, object txt) : BaseExpr(keyword)
{
    public readonly object Value = txt;

    public override string GetCodeText() => $"{Value}";
}

// 变量表达式
class VariableExpr(Token tag, bool readOnly = false) : TargetExpr(tag)
{
    public readonly string Tag = tag.Value;
    public readonly uint Reg = 0;

    public readonly bool ReadOnly = readOnly;

    public override string GetCodeText() => Tag;
}

// 常量表达式
sealed class ConstVarExpr(Token tag) : BaseExpr(tag)
{
    public readonly string Tag = tag.Value;
    public override string GetCodeText() => Tag;
}

sealed class ExtVarExpr(Token tag, string name) : BaseExpr(tag)
{
    public readonly string Name = name;

    public override string GetCodeText() => $"@{Name}";
}

sealed class BinaryExpression(Token op, BaseExpr left, BaseExpr right) : BaseExpr(op)
{
    public readonly BaseExpr ValueLeft = left;
    public readonly Token Operator = op;
    public readonly BaseExpr ValueRight = right;

    public override string GetCodeText()
    {
        return $"{ValueLeft.GetCodeText()} {Operator.Value} {ValueRight.GetCodeText()}";
    }
}

sealed class UnaryExpression(Token op, BaseExpr operand) : BaseExpr(op)
{
    public readonly Token Operator = op;
    public readonly BaseExpr Operand = operand;

    public override string GetCodeText()
    {
        return $"{Operator.Value}{Operand.GetCodeText()}";
    }
}

// (expr) ：右值
sealed class ParenthesizedExpression(Token lp, BaseExpr expression, Token rp) : BaseExpr(lp)
{
    public readonly BaseExpr Expression = expression;
    public readonly Token Lp = lp;
    public readonly Token Rp = rp;
    public override string GetCodeText() => $"({Expression.GetCodeText()})";
}

// 数组定义表达式 ：右值
sealed class IndexDefExpression(Token lb, ImmutableArray<BaseExpr> index, Token rb) : BaseExpr(lb)
{
    public ImmutableArray<BaseExpr> Index { get; } = index;
    public readonly Token Lb = lb;
    public readonly Token Rb = rb;
    public override string GetCodeText() => $"[{string.Join(", ", Index.Select(arg => arg.GetCodeText()))}]";
}

// 索引表达式
sealed class IndexVisitExpression(Token syntax, BaseExpr baseExpr, BaseExpr idxexpr) : TargetExpr(syntax)
{
    public readonly BaseExpr Base = baseExpr;
    public BaseExpr Index { get; } = idxexpr;
    public override string GetCodeText() => $"{Base.GetCodeText()}[{Index.GetCodeText()}]";
}

// 切片表达式 ：右值
sealed class SliceExpression(Token syntax, BaseExpr baseExpr, BaseExpr start, BaseExpr end, bool ommitstart) : BaseExpr(syntax)
{
    public readonly BaseExpr Base = baseExpr;
    public readonly BaseExpr Start = start;
    public readonly BaseExpr End = end;
    public override string GetCodeText() => $"{Base.GetCodeText()}[{(ommitstart ? "" : Start.GetCodeText())}:{End.GetCodeText()}]";
}

sealed class Callv1Expression(Token identifier, Token lp, ImmutableArray<BaseExpr> arguments, Token rp) : BaseExpr(identifier)
{
    public readonly Token Identifier = identifier;
    public readonly Token Lp = lp;
    public ImmutableArray<BaseExpr> Arguments { get; } = arguments;
    public readonly Token Rp = rp;
    public override string GetCodeText() => $"{Identifier.Value}({string.Join(", ", Arguments.Select(arg => arg.GetCodeText()))})";
}

// 结构体定义表达式 ：右值
sealed class StructInitExpr(Token syntax, string typeName) : BaseExpr(syntax)
{
    public readonly string TypeName = typeName;
    public override string GetCodeText() => $"{TypeName}{{}}";
}

// 属性访问表达式
sealed class FieldAccessExpr(Token syntax, BaseExpr target, string fieldName) : TargetExpr(syntax)
{
    public readonly BaseExpr Target = target;
    public readonly string FieldName = fieldName;
    public override string GetCodeText() => $"{Target.GetCodeText()}.{FieldName}";
}

// 占位符
sealed class DiscardExpr(Token syntax) : TargetExpr(syntax)
{
    public override string GetCodeText() => "_";
}