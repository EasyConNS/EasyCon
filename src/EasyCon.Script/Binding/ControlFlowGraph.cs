using EasyCon.Script.Symbols;

namespace EasyCon.Script.Binding;

internal sealed class ControlFlowGraph
{
    public static bool AllPathsReturn(BoundBlockStatement body)
    {
        return BlockAlwaysReturns(body);
    }

    private static bool BlockAlwaysReturns(BoundBlockStatement block)
    {
        foreach (var stmt in block.Statements)
        {
            if (StatementAlwaysReturns(stmt))
                return true;
        }
        return false;
    }

    private static bool StatementAlwaysReturns(BoundStmt stmt)
    {
        switch (stmt)
        {
            case BoundReturnStatement:
                return true;

            case BoundIfStatement ifStmt:
                if (!BlockAlwaysReturns(ifStmt.Body))
                    return false;
                foreach (var (_, elifBody) in ifStmt.ElseIfs)
                {
                    if (!BlockAlwaysReturns(elifBody))
                        return false;
                }
                return ifStmt.ElseBody != null && BlockAlwaysReturns(ifStmt.ElseBody);

            case BoundBlockStatement block:
                return BlockAlwaysReturns(block);

            default:
                return false;
        }
    }
}