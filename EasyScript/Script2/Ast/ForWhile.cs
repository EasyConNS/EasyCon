using VBF.Compilers.Scanners;

namespace ECP.Ast;

public class ForWhile : Statement
{
    public ForWhile(ForStatement forcond, Block statements)
    {
        Condition = forcond;
        BlockStmt = statements;
    }

    public ForStatement Condition { get; init; }
    public Block BlockStmt { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitForWhile(this);
    }
}

public class LoopControl : Statement
{
    public LoopControl(string type, LexemeValue level)
    {
        LoopType = type;
        Level = 1;
        if(level != null)
        {
            Level = int.Parse(level.Content);
        }
    }

    public string LoopType { get; init; }
    public int Level { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitLoopControl(this);
    }
}