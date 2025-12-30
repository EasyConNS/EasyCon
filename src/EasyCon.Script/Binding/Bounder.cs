using EasyScript.Statements;
using System.Collections.Immutable;

namespace EasyScript.Binding;

internal sealed class Binder
{
    //private readonly FunctionSymbol? _function;

    private Stack<(BoundLabel BreakLabel, BoundLabel ContinueLabel)> _loopStack = new Stack<(BoundLabel BreakLabel, BoundLabel ContinueLabel)>();
    private int _labelCounter;

    private Binder()
    {
        //TODO
    }

    public static void BoundTree(ImmutableArray<Statement> statements)
    {
        var binder = new Binder();
        foreach (var statement in statements)
        {
            binder.BindStatement(statement);
        }
    }

    private BoundStmt BindStatement(Statement statement)
    {
        if (statement is BlockStatement bs)
        {
            BindBlockStatement(bs);
        }
        else if (statement is ExpressionStmt expr)
        {
            // TODO
        }
        else if (statement is Break b)
        {
            BindBreakStatement(b);
        }
        else if (statement is Continue ctn)
        {
            BindContinueStatement(ctn);
        }
        else if (statement is ReturnStat rtns)
        {
            BindReturnStatement(rtns);
        }
        else if (statement is StickAction or KeyAction)
        {
            // TODO
        }
        throw new NotImplementedException();
    }

    private BoundStmt BindBlockStatement(BlockStatement stat)
    {
        switch (stat.Name)
        {
            case "If":
                return BindIf(stat);
            case "For":
                return BindFor(stat);
            case "Func":
                break;
            default:
                throw new Exception($"unexpected block name: {stat.Name}");
        }
        throw new NotImplementedException();
    }

    private BoundIf BindIf(BlockStatement block)
    {
        // gotoFalse <condition> end
        // <then>
        // end:
        //
        // gotoFalse <condition> else
        // <then>
        // goto end
        // else:
        // <else>
        // end:

        //如果line以"elif"开头，则结束当前子块（将当前子块加入列表），然后创建一个新的子块，关键字为"elif"，条件为line去掉"elif"关键字后的部分。
        //如果line以"else"开头，则结束当前子块，然后创建一个新的子块，关键字为"else"，条件为空。
        //如果line以"endif"开头，则结束当前子块（注意：endif本身不属于任何子块，所以遇到endif时，我们只结束当前子块，然后跳出循环）。
        //否则，将当前行添加到当前子块的语句列表中。
        var first = block.Statements[0];
        return new BoundIf(block);
    }

    private BoundFor BindFor(BlockStatement block)
    {
        //var lowerBound = BindExpression(syntax.LowerBound, TypeSymbol.Int);
        //var upperBound = BindExpression(syntax.UpperBound, TypeSymbol.Int);

        //_scope = new BoundScope(_scope);

        //var variable = BindVariableDeclaration(syntax.Identifier, isReadOnly: true, TypeSymbol.Int);
        //var body = BindLoopBody(syntax.Body, out var breakLabel, out var continueLabel);

        //_scope = _scope.Parent!;
        return new BoundFor(block);
    }

    private BoundStmt BindLoopBody(Statement body, out BoundLabel breakLabel, out BoundLabel continueLabel)
    {
        _labelCounter++;
        breakLabel = new BoundLabel($"break{_labelCounter}");
        continueLabel = new BoundLabel($"continue{_labelCounter}");

        _loopStack.Push((breakLabel, continueLabel));
        var boundBody = BindStatement(body);
        _loopStack.Pop();

        return boundBody;
    }

    private BoundStmt BindBreakStatement(Break syntax)
    {
        if (_loopStack.Count == 0)
        {
            throw new Exception();
        }

        var breakLabel = _loopStack.Peek().BreakLabel;
        return new BoundGotoStatement(syntax, breakLabel);
    }

    private BoundStmt BindContinueStatement(Continue syntax)
    {
        if (_loopStack.Count == 0)
        {
            throw new Exception();
        }

        var continueLabel = _loopStack.Peek().ContinueLabel;
        return new BoundGotoStatement(syntax, continueLabel);
    }

    private BoundStmt BindReturnStatement(ReturnStat syntax)
    {
        //if (_function == null)
        //{
        //    // Ignore because we allow both return with and without values.
        //}
        //else
        //{
        //    // 
        //}

        return new BoundReturnStatement(syntax);
    }
}