using EasyCon.Script.Symbols;
using System.Collections.Immutable;
using static EasyCon.Script.Binding.BoundNodeKind;

namespace EasyCon.Script.Binding;

internal sealed class Lowerer
{
    private int _labelCounter;

    private Lowerer() { }

    public static BoundBlockStatement Lower(FunctionSymbol fn, BoundStmt statement)
    {
        var lowerer = new Lowerer();
        var rewritten = lowerer.Rewrite(statement);
        return RemoveDeadCode(Flatten(fn, rewritten));
    }

    #region Rewrite: structured control flow → goto/label

    private BoundStmt Rewrite(BoundStmt stmt)
    {
        return stmt switch
        {
            BoundBlockStatement block => RewriteBlock(block),
            BoundIfStatement ifStmt => RewriteIf(ifStmt),
            BoundWhileStatement whileStmt => RewriteWhile(whileStmt),
            _ => stmt,
        };
    }

    private BoundBlockStatement RewriteBlock(BoundBlockStatement block)
    {
        if (block.Statements.IsEmpty)
            return block;

        var builder = ImmutableArray.CreateBuilder<BoundStmt>(block.Statements.Length);
        bool changed = false;
        foreach (var s in block.Statements)
        {
            var rewritten = Rewrite(s);
            changed = changed || !ReferenceEquals(rewritten, s);
            builder.Add(rewritten);
        }

        return changed
            ? new BoundBlockStatement(block.Syntax, builder.ToImmutable())
            : block;
    }

    // while <condition>
    //      <body>
    //
    // ----->
    //
    // goto continue
    // body:
    // <body>
    // continue:
    // gotoTrue <condition> body
    // break:
    private BoundBlockStatement RewriteWhile(BoundWhileStatement ws)
    {
        var bodyLabel = new BoundLabel($"body{++_labelCounter}");
        return BoundFactory.Block(ws.Syntax,
            BoundFactory.Goto(ws.Syntax, ws.ContinueLabel),
            BoundFactory.Label(ws.Syntax, bodyLabel),
            RewriteBlock(ws.Body),
            BoundFactory.Label(ws.Syntax, ws.ContinueLabel),
            BoundFactory.GotoTrue(ws.Syntax, bodyLabel, ws.Condition),
            BoundFactory.Label(ws.Syntax, ws.BreakLabel));
    }

    // IF <cond> ... ELSEIF <cond> ... ELSE ... ENDIF
    //
    // ----->
    //
    // GotoFalse <cond> nextLabel
    // <body>
    // Goto endLabel
    // nextLabel:
    // GotoFalse <elifCond> elifLabel
    // <elifBody>
    // Goto endLabel
    // elifLabel:
    // ...
    // <elseBody>
    // endLabel:
    private BoundBlockStatement RewriteIf(BoundIfStatement ifs)
    {
        _labelCounter++;
        var endLabel = new BoundLabel($"IfEnd_{_labelCounter}");
        var nextLabel = new BoundLabel($"NEXT_{_labelCounter}");

        var block = ImmutableArray.CreateBuilder<BoundStmt>();

        // IF branch
        block.Add(BoundFactory.GotoFalse(ifs.Syntax, nextLabel, ifs.Condition));
        block.Add(RewriteBlock(ifs.Body));
        block.Add(BoundFactory.Goto(ifs.Syntax, endLabel));
        block.Add(BoundFactory.Label(ifs.Syntax, nextLabel));

        // ELSEIF branches
        int elifCount = 0;
        foreach (var (condition, body) in ifs.ElseIfs)
        {
            var elifLabel = new BoundLabel($"ELIF_{_labelCounter}_{elifCount}");
            block.Add(BoundFactory.GotoFalse(ifs.Syntax, elifLabel, condition));
            block.Add(RewriteBlock(body));
            block.Add(BoundFactory.Goto(ifs.Syntax, endLabel));
            block.Add(BoundFactory.Label(ifs.Syntax, elifLabel));
            elifCount++;
        }

        // ELSE branch
        if (ifs.ElseBody != null)
        {
            block.Add(RewriteBlock(ifs.ElseBody));
        }

        block.Add(BoundFactory.Label(ifs.Syntax, endLabel));

        return BoundFactory.Block(ifs.Syntax, [.. block]);
    }

    #endregion

    #region Flatten

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

    #endregion

    #region RemoveDeadCode

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

    #endregion
}