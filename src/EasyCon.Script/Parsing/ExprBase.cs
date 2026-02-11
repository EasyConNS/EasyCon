using EasyCon.Script2.Syntax;
using System.Collections.Immutable;

namespace EasyCon.Script.Parsing;

// base of valuetype
abstract class ExprBase
{
    public abstract string GetCodeText();

    public static implicit operator ExprBase(int val)
    {
        return new LiteralExpr(val);
    }

    public static implicit operator ExprBase(string val)
    {
        return new LiteralExpr(val);
    }
}

class LiteralExpr(object txt) : ExprBase
{
    public readonly object Value = txt;

    public override string GetCodeText() => $"{Value}";

    public static implicit operator LiteralExpr(int val)
    {
        return new LiteralExpr(val);
    }

    // public static implicit operator int(LiteralExpr v)
    // {
    //     return (int)v.Value;
    // }
}

class VariableExpr : ExprBase
{
    public readonly string Tag;
    public readonly uint Reg;

    public readonly bool ReadOnly;

    public VariableExpr(string tag, bool readOnly = false)
    {
        Tag = tag;
        Reg = 0;
        ReadOnly = readOnly;
    }

    public VariableExpr(uint reg, bool readOnly = false)
    {
        Tag = reg.ToString();
        Reg = reg;
        ReadOnly = readOnly;
    }

    public override string GetCodeText() => Tag;
}

class ExtVarExpr(ExternalVariable var) : ExprBase
{
    public readonly ExternalVariable Var = var;

    public override string GetCodeText() => $"@{Var.Name}";
}

sealed class BinaryExpression(Token op, ExprBase left, ExprBase right) : ExprBase
{
    public readonly ExprBase ValueLeft = left;
    public readonly Token Operator = op;
    public readonly ExprBase ValueRight = right;

    public override string GetCodeText()
    {
        return $"{ValueLeft.GetCodeText()} {Operator.Value} {ValueRight.GetCodeText()}";
    }
}

sealed class UnaryExpression(Token op, ExprBase operand) : ExprBase
{
    public readonly Token Operator = op;
    public readonly ExprBase Operand = operand;

    public override string GetCodeText()
    {
        return $"{Operator.Value}{Operand.GetCodeText()}";
    }
}

sealed class ParenthesizedExpression(ExprBase expression) : ExprBase
{
    public readonly ExprBase Expression = expression;
    public override string GetCodeText() => $"({expression.GetCodeText()})";
}

sealed class Indexv1Expression(Token lb, ImmutableArray<ExprBase> index, Token rb) : ExprBase
{
    public readonly Token Lb = lb;
    public ImmutableArray<ExprBase> Index { get; } = index;
    public readonly Token Rb = rb;
    public override string GetCodeText() => $"[{string.Join(", ", Index.Select(arg => arg.GetCodeText()))}]";
}

sealed class SliceExpression(ExprBase expression, ExprBase start, ExprBase end) : ExprBase
{
    public readonly ExprBase Expression = expression;
    public readonly ExprBase Start = start;
    public readonly ExprBase End = end;
    public override string GetCodeText() => $"{Expression.GetCodeText()}[{Start.GetCodeText()}..{End.GetCodeText()}]";
}

sealed class Callv1Expression(Token identifier, Token lp, ImmutableArray<ExprBase> arguments, Token rp) : ExprBase
{
    public readonly Token Identifier = identifier;
    public readonly Token Lp = lp;
    public ImmutableArray<ExprBase> Arguments { get; } = arguments;
    public readonly Token Rp = rp;
    public override string GetCodeText() => $"{Identifier.Value}({string.Join(", ", Arguments.Select(arg => arg.GetCodeText()))})";
}