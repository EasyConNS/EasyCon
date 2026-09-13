using System.Collections.Immutable;

namespace EasyCon.Script.Syntax;

internal sealed class IfBlock(IfStmt condition, ImmutableArray<Statement> statements, EndBlockStmt endif) : Statement(condition.Syntax)
{
    public override StatementKind Kind => StatementKind.IfBlock;
    public readonly IfStmt Condition = condition;
    public ImmutableArray<Statement> Statements = statements;
    public readonly EndBlockStmt End = endif;

    protected override string _GetString()
    {
        throw new NotImplementedException();
    }
}

class IfStmt(Token syntax, BaseExpr conds) : StartBlockStmt(syntax)
{
    public override StatementKind Kind => StatementKind.IfStmt;
    public readonly BaseExpr Condition = conds;

    protected override string _GetString()
    {
        return $"IF {Condition.GetCodeText()}";
    }
}

class ElseIf(Token syntax, BaseExpr conds) : IfStmt(syntax, conds)
{
    public override StatementKind Kind => StatementKind.ElseIf;
    protected override string _GetString()
    {
        return $"ELIF {Condition.GetCodeText()}";
    }
}

class Else(Token syntax) : Statement(syntax)
{
    public override StatementKind Kind => StatementKind.Else;
    protected override string _GetString() => "ELSE";
}

class EndIf(Token syntax) : EndBlockStmt(syntax)
{
    public override StatementKind Kind => StatementKind.EndIf;
    protected override string _GetString() => "ENDIF";
}