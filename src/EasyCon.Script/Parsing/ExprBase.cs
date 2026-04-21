using System.Collections.Immutable;

namespace EasyCon.Script.Syntax;

// base of valuetype
abstract class ExprBase(Token syntax) : AstNode(syntax)
{
    public abstract string GetCodeText();

    // public static implicit operator ExprBase(int val) =>  new LiteralExpr(val);
    // public static implicit operator ExprBase(string val) => new LiteralExpr(val);
    // public static implicit operator ExprBase(bool val) => new LiteralExpr(val);
}

sealed class LiteralExpr(Token keyword, object txt) : ExprBase(keyword)
{
    public readonly object Value = txt;

    public override string GetCodeText() => $"{Value}";

    // public static implicit operator LiteralExpr(int val) => new(val);
    // public static implicit operator LiteralExpr(string val) => new(val);
    // public static implicit operator LiteralExpr(bool val) => new(val);
}

class VariableExpr(Token tag, bool readOnly = false) : ExprBase(tag)
{
    public readonly string Tag = tag.Value;
    public readonly uint Reg = 0;

    public readonly bool ReadOnly = readOnly;

    public override string GetCodeText() => Tag;
}

sealed class ConstVarExpr(Token tag) : VariableExpr(tag, true) { }

sealed class ExtVarExpr(Token tag, string name) : ExprBase(tag)
{
    public readonly string Name = name;

    public override string GetCodeText() => $"@{Name}";
}

sealed class BinaryExpression(Token op, ExprBase left, ExprBase right) : ExprBase(op)
{
    public readonly ExprBase ValueLeft = left;
    public readonly Token Operator = op;
    public readonly ExprBase ValueRight = right;

    public override string GetCodeText()
    {
        return $"{ValueLeft.GetCodeText()} {Operator.Value} {ValueRight.GetCodeText()}";
    }
}

sealed class UnaryExpression(Token op, ExprBase operand) : ExprBase(op)
{
    public readonly Token Operator = op;
    public readonly ExprBase Operand = operand;

    public override string GetCodeText()
    {
        return $"{Operator.Value}{Operand.GetCodeText()}";
    }
}

sealed class ParenthesizedExpression(Token lp, ExprBase expression, Token rp) : ExprBase(lp)
{
    public readonly ExprBase Expression = expression;
    public readonly Token Lp = lp;
    public readonly Token Rp = rp;
    public override string GetCodeText() => $"({Expression.GetCodeText()})";
}

sealed class IndexDefExpression(Token lb, ImmutableArray<ExprBase> index, Token rb) : ExprBase(lb)
{
    public ImmutableArray<ExprBase> Index { get; } = index;
    public readonly Token Lb = lb;
    public readonly Token Rb = rb;
    public override string GetCodeText() => $"[{string.Join(", ", Index.Select(arg => arg.GetCodeText()))}]";
}

sealed class IndexVisitExpression(Token var, Token lb, ExprBase idxexpr, Token rb) : VariableExpr(var)
{
    public readonly Token Lb = lb;
    public ExprBase Index { get; } = idxexpr;
    public readonly Token Rb = rb;
    public override string GetCodeText() => $"{Tag}[{Index.GetCodeText()}]";
}

sealed class SliceExpression(Token var, ExprBase start, ExprBase end, bool ommitstart) : VariableExpr(var)
{
    public readonly ExprBase Start = start;
    public readonly ExprBase End = end;
    public override string GetCodeText() => $"{Tag}[{(ommitstart ? "" : Start.GetCodeText())}:{End.GetCodeText()}]";
}

sealed class Callv1Expression(Token identifier, Token lp, ImmutableArray<ExprBase> arguments, Token rp) : ExprBase(identifier)
{
    public readonly Token Identifier = identifier;
    public readonly Token Lp = lp;
    public ImmutableArray<ExprBase> Arguments { get; } = arguments;
    public readonly Token Rp = rp;
    public override string GetCodeText() => $"{Identifier.Value}({string.Join(", ", Arguments.Select(arg => arg.GetCodeText()))})";
}