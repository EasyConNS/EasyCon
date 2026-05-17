using System.Collections.Immutable;

namespace EasyCon.Script.Syntax;

internal sealed class ForBlock(ForStmt condition, ImmutableArray<Statement> statements, EndBlockStmt end) : Statement(condition.Syntax)
{
    public override StatementKind Kind => StatementKind.ForBlock;
    public readonly ForStmt Condition = condition;
    public ImmutableArray<Statement> Statements = statements;
    public readonly EndBlockStmt End = end;

    protected override string _GetString()
    {
        throw new NotImplementedException();
    }
}

abstract class ForStmt(Token syntax, BaseExpr lower, BaseExpr upper) : StartBlockStmt(syntax)
{
    public override StatementKind Kind => StatementKind.ForStmt;
    public readonly BaseExpr Lower = lower;
    public readonly BaseExpr Upper = upper;
}

class For_Infinite(Token syntax) : ForStmt(syntax, new LiteralExpr(syntax, 0), new LiteralExpr(syntax, 0))
{

    public override StatementKind Kind => StatementKind.ForStmt;
    protected override string _GetString() => "FOR";
}

class For_Static(Token syntax, BaseExpr count) : ForStmt(syntax, new LiteralExpr(syntax, 1), count)
{
    public override StatementKind Kind => StatementKind.ForStmt;
    protected override string _GetString()
    {
        return $"FOR {Upper.GetCodeText()}";
    }
}

class For_Full(Token syntax, VariableExpr regiter, BaseExpr lower, BaseExpr upper) : ForStmt(syntax, lower, upper)
{
    public override StatementKind Kind => StatementKind.ForStmt;
    public VariableExpr RegIter = regiter;

    protected override string _GetString()
    {
        return $"FOR {RegIter.GetCodeText()} = {Lower.GetCodeText()} TO {Upper.GetCodeText()}";
    }
}

class Next(Token syntax) : EndBlockStmt(syntax)
{
    public override StatementKind Kind => StatementKind.Next;

    // 覆盖基类的 _GetString
    protected override string _GetString() => "NEXT";
}

internal sealed class WhileBlock(WhileStmt condition, ImmutableArray<Statement> statements, EndBlockStmt end) : Statement(condition.Syntax)
{
    public override StatementKind Kind => StatementKind.WhileBlock;
    public readonly WhileStmt Condition = condition;
    public ImmutableArray<Statement> Statements = statements;
    public readonly EndBlockStmt End = end;

    protected override string _GetString()
    {
        throw new NotImplementedException();
    }
}

class WhileStmt(Token syntax, BaseExpr conds) : StartBlockStmt(syntax)
{
    public override StatementKind Kind => StatementKind.WhileStmt;
    public readonly BaseExpr Condition = conds;
    protected override string _GetString()
    {
        return $"WHILE {Condition.GetCodeText()}";
    }
}

internal sealed class UntilBlock(UntilStmt condition, ImmutableArray<Statement> statements, EndBlockStmt end) : Statement(condition.Syntax)
{
    public override StatementKind Kind => StatementKind.UntilBlock;
    public readonly UntilStmt Condition = condition;
    public ImmutableArray<Statement> Statements = statements;
    public readonly EndBlockStmt End = end;

    protected override string _GetString()
    {
        throw new NotImplementedException();
    }
}

class UntilStmt(Token syntax, BaseExpr conds) : StartBlockStmt(syntax)
{
    public override StatementKind Kind => StatementKind.UntilStmt;
    public readonly BaseExpr Condition = conds;
    protected override string _GetString()
    {
        return $"UNTIL {Condition.GetCodeText()}";
    }
}

abstract class LoopCtrl(Token syntax, uint level) : Statement(syntax)
{
    public readonly uint Level = level;
}

class Break : LoopCtrl
{
    public Break(Token syntax) : base(syntax, 1) { }

    public Break(Token syntax, uint level) : base(syntax, level) { }

    public override StatementKind Kind => StatementKind.Break;
    protected override string _GetString()
    {
        if (Level == 1) return "BREAK";
        return $"BREAK {Level}";
    }
}

class Continue : LoopCtrl
{
    public Continue(Token syntax) : base(syntax, 1) { }

    public override StatementKind Kind => StatementKind.Continue;
    protected override string _GetString() => "CONTINUE";
}