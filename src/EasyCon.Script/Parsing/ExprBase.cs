namespace EasyScript.Parsing;

// base of valuetype
abstract class ExprBase
{
    public abstract int Get(Processor processor);

    public virtual void Set(Processor processor, int value)
    {
        throw new InvalidOperationException();
    }

    public abstract string GetCodeText();

    public static implicit operator ExprBase(int val)
    {
        return new InstantExpr(val);
    }
}

// instant number, including constant
class InstantExpr : ExprBase
{
    public readonly int Val;
    public readonly string Text;

    public InstantExpr(int val)
    {
        Val = val;
        Text = val.ToString();
    }

    public InstantExpr(int val, string text)
    {
        Val = val;
        Text = text;
    }

    public override int Get(Processor _)
    {
        return Val;
    }

    public override string GetCodeText()
    {
        return Text;
    }

    public static implicit operator InstantExpr(int val)
    {
        return new InstantExpr(val);
    }

    public static implicit operator int(InstantExpr v)
    {
        return v.Val;
    }
}

// register variable, either 16 or 32 bits
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

    public override void Set(Processor processor, int value)
    {
        processor.Register[this] = value;
    }

    public override int Get(Processor processor)
    {
        return processor.Register[this];
    }

    public override string GetCodeText()
    {
        return $"${Tag}";
    }
}

// external variable 
class ExtVarExpr(ExternalVariable var) : ExprBase
{
    public readonly ExternalVariable Var = var;

    public override int Get(Processor _)
    {
        return Var.Get();
    }

    public override string GetCodeText()
    {
        return $"@{Var.Name}";
    }
}

class BinaryExpression(MetaOperator op, ExprBase left, ExprBase right, bool hasPr = false) : ExprBase
{
    protected readonly ExprBase ValueLeft = left;
    protected readonly MetaOperator OpMeta = op;
    protected readonly ExprBase ValueRight = right;

    protected readonly bool HasPr = hasPr;
    public override int Get(Processor processor)
    {
        return OpMeta.Function(ValueLeft.Get(processor), ValueRight.Get(processor));
    }

    public override string GetCodeText()
    {
        var exp = $"{ValueLeft.GetCodeText()} {OpMeta.Operator} {ValueRight.GetCodeText()}";
        return HasPr ? $"({exp})" : exp;
    }

    public static ExprBase Rewrite(ExprBase expr)
    {
        if (expr is BinaryExpression br)
        {
            var left = Rewrite(br.ValueLeft);
            var right = Rewrite(br.ValueRight);
            if(left is InstantExpr li && right is InstantExpr ri)
            {
                return br.OpMeta.Function(li.Val, ri.Val);
            }
            else
            {
                return new BinaryExpression(br.OpMeta, left, right, br.HasPr);
            }
        }
        return expr;
    }
}


class CmpExpression(CompareOperator op, ExprBase left, ExprBase right, bool hasPr = false) : ExprBase
{
    protected readonly ExprBase Value1 = left;
    protected readonly CompareOperator CmpOp = op;
    protected readonly ExprBase Value2 = right;
    protected readonly bool HasPr = hasPr;

    public override int Get(Processor processor)
    {
        var cond = CmpOp.Compare(Value1.Get(processor), Value2.Get(processor));
        return cond ? 1 : 0;
    }

    public override string GetCodeText()
    {

        var op = CmpOp.Operator == "=" ? "==" : CmpOp.Operator;

        var exp = $"{Value1.GetCodeText()} {op} {Value2.GetCodeText()}";
        return HasPr ? $"({exp})" : exp;
    }
}