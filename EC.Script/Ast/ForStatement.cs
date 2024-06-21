using VBF.Compilers.Scanners;

namespace ECP.Ast;

public class ForStatement : Expression
{
    public ForStatement(Number count)
    {
        LoopCount = count;
    }

    public Number LoopCount { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitForCondition(this);
    }
}

public class ForStatementFull : ForStatement
{
    public ForStatementFull(LexemeValue initvar, Number start, Number end) : base(end)
    {
        Var = initvar;
        Start = start;
    }

    public LexemeValue Var { get; init; }
    public Number Start { get; init; }
}
