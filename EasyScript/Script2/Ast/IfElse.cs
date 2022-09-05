using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ECP.Ast;

public class IfElse : Statement
{
    public IfElse(Expression condition, Block statements, IList<ElseIf> elifStmt, Block elsestmt)
    {
        Condition = condition;
        if(elifStmt != null)
        {
            ElifStmt = new ReadOnlyCollection<ElseIf>(elifStmt);
        }
        if(elsestmt != null)
        {
            ElseStmt = elsestmt;
        }
        BlockStmt = statements;
    }

    public Expression Condition { get; init; }
    public Block BlockStmt { get; init; }
    public ReadOnlyCollection<ElseIf> ElifStmt { get; init; }
    public Block ElseStmt { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitIfElse(this);
    }
}

public class ElseIf : Statement
{
    public ElseIf(Expression condition, Block statements)
    {
        Condition = condition;
        BlockStmt = statements;
    }

    public Expression Condition { get; init; }
    public Block BlockStmt { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitElseIf(this);
    }
}