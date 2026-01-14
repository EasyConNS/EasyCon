using EasyCon.Script2.Syntax;

namespace EasyCon.Script.Parsing;

// base of valuetype
abstract class ExprBase
{
    public abstract string GetCodeText();

    public static implicit operator ExprBase(int val)
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

    public static implicit operator int(LiteralExpr v)
    {
        return (int)v.Value;
    }
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

    // public static ExprBase Rewrite(ExprBase expr)
    // {
    //     if (expr is BinaryExpression br)
    //     {
    //         var left = Rewrite(br.ValueLeft);
    //         var right = Rewrite(br.ValueRight);
    //         if (left is InstantExpr li && right is InstantExpr ri)
    //         {
    //            return br.OpMeta.Function(li.Value, ri.Value);
    //         }
    //         else
    //         {
    //            return new BinaryExpression(br.OpMeta, left, right);
    //         }
    //     }
    //     return expr;
    // }
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