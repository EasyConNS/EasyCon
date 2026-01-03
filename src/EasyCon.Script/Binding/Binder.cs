using EasyCon.Script.Parsing;
using System.Collections.Immutable;
using static EasyCon.Script.Binding.BoundFactory;

namespace EasyCon.Script.Binding;

internal sealed class Binder
{
    private readonly FunctionSymbol? _function;

    private Stack<(BoundLabel BreakLabel, BoundLabel ContinueLabel)> _loopStack = new();
    private int _labelCounter = 0;

    private BoundScope _scope;

    private Binder(BoundScope? parent, FunctionSymbol? function)
    {
        _scope = new BoundScope(parent);
        _function = function;

        if (function != null)
        {
            // try declare function paramters
        }
    }

    public static BoundProgram BindProgram(ImmutableArray<CompicationUnit> syntaxs)
    {
        var parentScope = CreateRootScope();
        var binder = new Binder(parentScope, function: null);

        var functionDeclarations = syntaxs.SelectMany(st=>st.Members).OfType<FuncDeclBlock>();

        foreach (var function in functionDeclarations)
            binder.BindFuncDeclaration(function);

        var firstGlobalStatementPerSyntaxTree = syntaxs.Select(st => st.Members.Where(m => m is not FuncDeclBlock).FirstOrDefault())
                                                                .Where(g => g != null)
                                                                .ToArray();
        if (firstGlobalStatementPerSyntaxTree.Length > 1) 
            throw new ParseException("脚本主语句只能存在一个文件中", firstGlobalStatementPerSyntaxTree.First()!.Address);

        var globalStatements = syntaxs.SelectMany(st => st.Members)
                                              .Where(m => m is not FuncDeclBlock);

        var statements = ImmutableArray.CreateBuilder<BoundStmt>();

        foreach (var globalStatement in globalStatements)
        {
            var statement = binder.BindStatement(globalStatement);
            statements.Add(statement);
        }
        return null;
    }

    private void BindFuncDeclaration(FuncDeclBlock syntax)
    {
        //
    }

    private static BoundScope CreateRootScope()
    {
        var result = new BoundScope(null);

        foreach (var f in BuiltinFunctions.GetAll())
            result.TryDeclareFunction(f);

        return result;
    }

    private BoundStmt BindStatement(Statement syntax)
    {
        switch (syntax)
        {
            case IfBlock:
                return BindIf((IfBlock)syntax);
            case ForBlock:
                return BindFor((ForBlock)syntax);
            case AssignmentStmt:
                return BindAssignStatement((AssignmentStmt)syntax);
            case KeyAction:
                return BindGamepadActionStatement((KeyAction)syntax);
            case Break:
                return BindBreakStatement((Break)syntax);
            case Continue:
                return BindContinueStatement((Continue)syntax);
            case ReturnStmt:
                return BindReturnStatement((ReturnStmt)syntax);
            default:
                throw new ParseException($"未知的语句", syntax.Address);
        }
    }
    private BoundBlockStatement BindIf(IfBlock syntax)
    {
        _labelCounter++;
        var endLabel = new BoundLabel($"IfEnd_{_labelCounter}");
        var nextLabel = new BoundLabel($"NEXT_{_labelCounter}");

        var block = new List<BoundStmt>
        {
            GotoFalse(syntax, nextLabel, /*syntax.Condition.Condition*/null) // if not goto nextlabel
        };
        static bool isCtrl(Statement st)
        {
            if (st is ElseIf || st is Else || st is EndIf) return true;
            return false;
        }
        var index = 0;
        while(index < syntax.Statements.Length && !isCtrl(syntax.Statements[index]))
        {
            block.Add(BindStatement(syntax.Statements[index]));
            index++;
        }
        block.Add(Goto(syntax, endLabel));
        block.Add(Label(syntax, nextLabel));
        // 处理 elif 分支
        int elifCount = 0;
        while (index < syntax.Statements.Length && syntax.Statements[index] is ElseIf elifCond)
        {
            var elifLabel = new BoundLabel($"ELIF_{_labelCounter}_{elifCount}");
            block.Add(GotoFalse(syntax, elifLabel, /*elifCond.Condition*/null));

            index++;
            while (index < syntax.Statements.Length && !isCtrl(syntax.Statements[index]))
            {
                block.Add(BindStatement(syntax.Statements[index]));
                index++;
            }

            block.Add(Goto(syntax, endLabel));
            block.Add(Label(syntax, elifLabel));
            elifCount++;
        }
        // 处理 else 分支
        if (index < syntax.Statements.Length && syntax.Statements[index] is Else)
        {
            index++;
            while (index < syntax.Statements.Length && !isCtrl(syntax.Statements[index]))
            {
                block.Add(BindStatement(syntax.Statements[index]));
                index++;
            }
        }

        // 跳过 endif
        if (index < syntax.Statements.Length && syntax.Statements[index] is EndIf)
        {
            index++;
        }
        if (index != syntax.Statements.Length) throw new ParseException("if语句解析错误", syntax.Condition.Address);

        return Block(syntax, [.. block, Label(syntax, endLabel)]);
    }

    private BoundBlockStatement BindFor(ForBlock syntax)
    {
        _labelCounter++;
        var bodyLabel = new BoundLabel($"body{_labelCounter}");
        var conditionLabel = new BoundLabel($"condition{_labelCounter}");

        //var lowerBound = VariableDeclaration(node.Syntax, node.Variable, node.LowerBound);
        //var upperBound = ConstantDeclaration(node.Syntax, "upperBound", node.UpperBound);

        _scope = new BoundScope(_scope);
        var body = BindLoopBody(syntax, syntax.Statements, out var breakLabel, out var continueLabel);
        _scope = _scope.Parent!;

        return Block(syntax,
             //lowerBound,//      var <var> = <lower>
             //upperBound,//      let upperBound = <upper>
            Goto(syntax, conditionLabel),
            Label(syntax, bodyLabel),
            body,
            Label(syntax, continueLabel),
            //          <var> = <var> + <step>
            Label(syntax, conditionLabel),
            GotoTrue(syntax, bodyLabel, /*<var> <= upperBound*/null),
            Label(syntax, breakLabel));
    }

    private BoundBlockStatement BindLoopBody(Statement syntax, ImmutableArray<Statement> body, out BoundLabel breakLabel, out BoundLabel continueLabel)
    {
        _labelCounter++;
        breakLabel = new BoundLabel($"break{_labelCounter}");
        continueLabel = new BoundLabel($"continue{_labelCounter}");

        _loopStack.Push((breakLabel, continueLabel));
        var boundBody = Block(syntax, [.. body.Select(b => BindStatement(b))]);
        _loopStack.Pop();

        return boundBody;
    }
    
    private BoundGotoStatement BindBreakStatement(Break syntax)
    {
        if (_loopStack.Count < syntax.Level.Value)
        {
            throw new ParseException("循环层数不足", syntax.Address);
        }

        var breakLabel = _loopStack.ElementAt(syntax.Level.Value - 1).BreakLabel;
        return new BoundGotoStatement(syntax, breakLabel);
    }

    private BoundGotoStatement BindContinueStatement(Continue syntax)
    {
        if (_loopStack.Count == 0)
        {
            throw new ParseException("循环层数不足", syntax.Address);
        }

        var continueLabel = _loopStack.Peek().ContinueLabel;
        return new BoundGotoStatement(syntax, continueLabel);
    }

    private BoundReturnStatement BindReturnStatement(ReturnStmt syntax) => new BoundReturnStatement(syntax);

    private BoundAssignStatement BindAssignStatement(AssignmentStmt syntax)
    {
        var boundexpr = BindExpression(syntax.Expression);
        var variable = _scope.TryLookupSymbol(syntax.DestVariable.Tag) ?? new GlobalVariableSymbol(syntax.DestVariable.Tag);

        return new BoundAssignStatement(syntax, (VariableSymbol)variable, boundexpr);
    }

    private BoundKeyActStatement BindGamepadActionStatement(KeyAction syntax)
    {
        throw new NotImplementedException();
    }

    private BoundExpr BindExpression(ExprBase syntax)
    {
        throw new NotImplementedException();
    }

    private VariableSymbol? BindVariableReference(string name)
    {
        switch (_scope.TryLookupSymbol(name))
        {
            case VariableSymbol variable:
                return variable;

            case null:
                //_diagnostics.ReportUndefinedVariable(identifierToken.Location, name);
                return null;

            default:
                //_diagnostics.ReportNotAVariable(identifierToken.Location, name);
                return null;
        }
    }
}
