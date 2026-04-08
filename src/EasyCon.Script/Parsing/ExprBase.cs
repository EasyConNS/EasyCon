using System.Collections.Immutable;

namespace EasyCon.Script.Syntax;

// base of valuetype
abstract class ExprBase
{
    public abstract string GetCodeText();

    public static implicit operator ExprBase(int val) =>  new LiteralExpr(val);

    public static implicit operator ExprBase(string val) => new LiteralExpr(val);

    public static implicit operator ExprBase(bool val) => new LiteralExpr(val);
}

class LiteralExpr(object txt) : ExprBase
{
    public readonly object Value = txt;

    public override string GetCodeText() => $"{Value}";

    public static implicit operator LiteralExpr(int val) => new(val);
    public static implicit operator LiteralExpr(string val) => new(val);
    public static implicit operator LiteralExpr(bool val) => new(val);
}

class VariableExpr(string tag, bool readOnly = false) : ExprBase
{
    public readonly string Tag = tag;
    public readonly uint Reg = 0;

    public readonly bool ReadOnly = readOnly;

    public override string GetCodeText() => Tag;
}

class ConstVarExpr(string tag) : VariableExpr(tag, true) {}

class ExtVarExpr(string name) : ExprBase
{
    public readonly string Name = name;

    public override string GetCodeText() => $"@{Name}";
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

sealed class ParenthesizedExpression(Token lp, ExprBase expression, Token rp) : ExprBase
{
    public readonly ExprBase Expression = expression;
    public readonly Token Lp = lp;
    public readonly Token Rp = rp;
    public override string GetCodeText() => $"({Expression.GetCodeText()})";
}

sealed class IndexDefExpression(Token lb, ImmutableArray<ExprBase> index, Token rb) : ExprBase
{
    public ImmutableArray<ExprBase> Index { get; } = index;
    public readonly Token Lb = lb;
    public readonly Token Rb = rb;
    public override string GetCodeText() => $"[{string.Join(", ", Index.Select(arg => arg.GetCodeText()))}]";
}

sealed class IndexVisitExpression(VariableExpr var, Token lb, ExprBase idxexpr, Token rb) : ExprBase
{
    public readonly VariableExpr Var = var;
    public readonly Token Lb = lb;
    public ExprBase Index { get; } = idxexpr;
    public readonly Token Rb = rb;
    public override string GetCodeText() => $"{Var.GetCodeText()}[{Index.GetCodeText()}]";
}

sealed class SliceExpression(VariableExpr var, ExprBase start, ExprBase end, bool ommitstart) : ExprBase
{
    public readonly VariableExpr Var = var;
    public readonly ExprBase Start = start;
    public readonly ExprBase End = end;
    public override string GetCodeText() => $"{Var.GetCodeText()}[{(ommitstart? "" : Start.GetCodeText())}:{End.GetCodeText()}]";
}

sealed class Callv1Expression(Token identifier, Token lp, ImmutableArray<ExprBase> arguments, Token rp) : ExprBase
{
    public readonly Token Identifier = identifier;
    public readonly Token Lp = lp;
    public ImmutableArray<ExprBase> Arguments { get; } = arguments;
    public readonly Token Rp = rp;
    public override string GetCodeText() => $"{Identifier.Value}({string.Join(", ", Arguments.Select(arg => arg.GetCodeText()))})";
}