using EasyCon.Script.Symbols;
using System.Collections.Immutable;
using static EasyCon.Script.Binding.BoundNodeKind;

namespace EasyCon.Script.Binding;

internal sealed class Lowerer
{
    public static BoundBlockStatement Lower(FunctionSymbol fn, BoundStmt statement)
    {
        return RemoveDeadCode(Flatten(fn, statement));
    }

    private static BoundBlockStatement Flatten(FunctionSymbol fn, BoundStmt statement)
    {
        var builder = ImmutableArray.CreateBuilder<BoundStmt>();
        var stack = new Stack<BoundStmt>();
        stack.Push(statement);

        while (stack.Count > 0)
        {
            var current = stack.Pop();

            if (current is BoundBlockStatement block)
            {
                foreach (var s in block.Statements.Reverse())
                {
                    if (s.Kind != NopStatement) stack.Push(s);
                }
            }
            else
            {
                builder.Add(current);
            }
        }
        if (fn.ReturnType == ScriptType.Void)
        {
            if (builder.Count == 0 || CanFallThrough(builder.Last()))
            {
                builder.Add(new BoundReturnStatement(statement.Syntax, null));
            }
        }
        return new BoundBlockStatement(statement.Syntax, builder.ToImmutable());
    }

    private static bool CanFallThrough(BoundStmt boundStatement)
    {
        return boundStatement.Kind != Return &&
                boundStatement.Kind != GotoStatement;
    }

    private static BoundBlockStatement RemoveDeadCode(BoundBlockStatement statement)
    {
        var controlFlow = ControlFlowGraph.Create(statement);
        var reachableStatements = new HashSet<BoundStmt>(
            controlFlow.Blocks.SelectMany(b => b.Statements));

        var builder = statement.Statements.ToBuilder();
        for (int i = builder.Count - 1; i >= 0; i--)
        {
            if (!reachableStatements.Contains(builder[i]))
                builder.RemoveAt(i);
        }

        return new BoundBlockStatement(statement.Syntax, builder.ToImmutable());
    }
}