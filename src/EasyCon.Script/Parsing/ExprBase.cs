using EasyCon.Script2.Syntax;

namespace EasyCon.Script.Parsing;

// base of valuetype
abstract class ExprBase
{
    public abstract string GetCodeText();

    public static implicit operator ExprBase(int val)
    {
        return new InstantExpr(val);
    }
}

class LiteralExpr(string txt) : ExprBase
{
    public readonly string Text = txt;

    public override string GetCodeText() => Text;
}

// instant number, including constant
class InstantExpr : ExprBase
{
    public readonly int Value;
    public readonly string Text;

    public InstantExpr(int val)
    {
        Value = val;
        Text = val.ToString();
    }

    public InstantExpr(int val, string text)
    {
        Value = val;
        Text = text;
    }

    public override string GetCodeText() => Text;

    public static implicit operator InstantExpr(int val)
    {
        return new InstantExpr(val);
    }

    public static implicit operator int(InstantExpr v)
    {
        return v.Value;
    }
}

class VariableExpr : ExprBase
{
    public readonly string Tag;
    public readonly uint Reg;

    public VariableExpr(string tag)
    {
        Tag = tag;
        Reg = 0;
    }

    public VariableExpr(uint reg)
    {
        Tag = reg.ToString();
        Reg = reg;
    }

    public override string GetCodeText() => $"${Tag}";
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
        return $"{ValueLeft.GetCodeText()} {Operator} {ValueRight.GetCodeText()}";
    }

    public static ExprBase Rewrite(ExprBase expr)
    {
        if (expr is BinaryExpression br)
        {
            var left = Rewrite(br.ValueLeft);
            var right = Rewrite(br.ValueRight);
            //if (left is InstantExpr li && right is InstantExpr ri)
            //{
            //    return br.OpMeta.Function(li.Value, ri.Value);
            //}
            //else
            //{
            //    return new BinaryExpression(br.OpMeta, left, right);
            //}
        }
        return expr;
    }
}

sealed class ParenthesizedExpression(ExprBase expression) : ExprBase
{
    public readonly ExprBase Expression = expression;
    public override string GetCodeText() => $"({expression.GetCodeText()})";
}