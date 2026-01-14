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

        var functionBodies = ImmutableDictionary.CreateBuilder<FunctionSymbol, BoundBlockStatement>();

        var functionDeclarations = syntaxs.SelectMany(st => st.Members).OfType<FuncDeclBlock>();

        foreach (var function in functionDeclarations)
        {
            binder.BindFuncDeclaration(function);
        }

        foreach (var function in binder._scope.GetDeclaredFunctions())
        {
            var binderFn = new Binder(parentScope, function);
            var fnstatements = ImmutableArray.CreateBuilder<BoundStmt>();
            foreach (var fnStatement in function.Declaration!.Statements)
            {
                var statement = binderFn.BindStatement(fnStatement);
                fnstatements.Add(statement);
            }
            var fnbody = new BoundBlockStatement(function.Declaration, fnstatements.ToImmutable());
            var fnloweredBody = Lowerer.Flatten(fnbody);

            functionBodies.Add(function, fnloweredBody);
        }

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
        var main = new FunctionSymbol("$eval");
        var body = new BoundBlockStatement(null, statements.ToImmutable());
        var loweredBody = Lowerer.Flatten(body);
        functionBodies.Add(main, loweredBody);
        return new BoundProgram(main, functionBodies.ToImmutable());
    }

    private void BindFuncDeclaration(FuncDeclBlock syntax)
    {
        var function = new FunctionSymbol(syntax.Declare.Name);
        _scope.TryDeclareFunction(function);
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
        try
        {
            return syntax switch
            {
                IfBlock => BindIf((IfBlock)syntax),
                ForBlock => BindFor((ForBlock)syntax),
                ConstDeclStmt => throw new NotImplementedException(),
                AssignmentStmt => BindAssignStatement((AssignmentStmt)syntax),
                KeyAction => BindGamepadActionStatement((KeyAction)syntax),
                Break => BindBreakStatement((Break)syntax),
                Continue => BindContinueStatement((Continue)syntax),
                ReturnStmt => new BoundReturnStatement(syntax),
                CallStmt => BindCallStatement((CallStmt)syntax),
                _ => throw new ParseException("未知的语句", syntax.Address),
            };
        }
        catch (Exception ex)
        {
            throw new ParseException(ex.Message, syntax.Address);
        }
    }
    private BoundBlockStatement BindIf(IfBlock syntax)
    {
        _labelCounter++;
        var endLabel = new BoundLabel($"IfEnd_{_labelCounter}");
        var nextLabel = new BoundLabel($"NEXT_{_labelCounter}");

        var block = new List<BoundStmt>
        {
            GotoFalse(syntax, nextLabel, BindExpression(syntax.Condition.Condition))
        };
        static bool isCtrl(Statement st)
        {
            if (st is ElseIf || st is Else || st is EndIf) return true;
            return false;
        }
        var index = 0;
        while (index < syntax.Statements.Length && !isCtrl(syntax.Statements[index]))
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
            block.Add(GotoFalse(syntax, elifLabel, BindExpression(elifCond.Condition)));

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
        BoundStmt lowerBound = null;// VariableDeclaration(node.Syntax, node.Variable, node.LowerBound);
        BoundStmt upperBound = null; // ConstantDeclaration(node.Syntax, "upperBound", node.UpperBound);

        _scope = new BoundScope(_scope);
        var body = BindLoopBody(syntax, syntax.Statements, out var breakLabel, out var continueLabel);
        _scope = _scope.Parent!;

        var lowwhile = new BoundWhileStatement(syntax,
             /*<var> <= upperBound*/new BoundBinaryExpression(null, null, null, null),
             Block(syntax,
                body,
                Label(syntax, continueLabel),
                /*  <var> = <var> + <step>*/new BoundAssignStatement(syntax, null, null)
                ),
             breakLabel,
             new BoundLabel($"label{++_labelCounter}")
             );

        return Block(syntax,
            lowerBound,//      var <var> = <lower>
            upperBound,//      let upperBound = <upper>
            BindWhile(lowwhile)
             );
    }

    private BoundBlockStatement BindWhile(BoundWhileStatement boundsyntax)
    {
        var bodyLabel = new BoundLabel($"body{++_labelCounter}");
        var syntax = boundsyntax.Syntax;
        return Block(syntax,
                Goto(syntax, boundsyntax.ContinueLabel),
                Label(syntax, bodyLabel),
                boundsyntax.Body,
                Label(syntax, boundsyntax.ContinueLabel),
                GotoTrue(syntax, bodyLabel, boundsyntax.Condition),
                Label(syntax, boundsyntax.BreakLabel));
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
        var level = (int)BindExpression(syntax.Level).ConstantValue;
        if (_loopStack.Count < level)
        {
            throw new ParseException("循环层数不足", syntax.Address);
        }

        var breakLabel = _loopStack.ElementAt(level - 1).BreakLabel;
        // var breakLabel = _loopStack.Peek().BreakLabel;
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

    private BoundAssignStatement BindAssignStatement(AssignmentStmt syntax)
    {
        var boundexpr = BindExpression(syntax.Expression);

        if (syntax.AugOp != null)
        {
            var desvar = _scope.TryLookupSymbol(syntax.DestVariable.Tag) ?? throw new ParseException($"未定义的变量 {syntax.DestVariable.Tag}", syntax.Address);
            var vardesvar = desvar as VariableSymbol;
            var op = BoundBinaryOperator.Bind(syntax.AugOp.Operator);
            boundexpr = new BoundBinaryExpression(syntax.Expression, new BoundVariableExpression(syntax.Expression, vardesvar!), op!, boundexpr);
        }
        var variable = BindVariableDeclaration(syntax.DestVariable, false, boundexpr.Type);

        if (variable.Type != boundexpr.Type) throw new ParseException("表达式和变量类型不匹配", syntax.Address);

        return new BoundAssignStatement(syntax, variable, boundexpr);
    }

    private VariableSymbol BindVariableDeclaration(VariableExpr syntax, bool isReadOnly, ValueType type)
    {
        if (_scope.TryLookupSymbol(syntax.Tag) is VariableSymbol vvr) return vvr;
        var variable = _function == null
                    ? (VariableSymbol)new GlobalVariableSymbol(syntax.Tag, isReadOnly, type)
                    : new LocalVariableSymbol(syntax.Tag, isReadOnly, type);

        _scope.TryDeclareVariable(variable);

        return variable;
    }

    private BoundCallStatement BindCallStatement(CallStmt syntax)
    {
        var symbol = _scope.TryLookupSymbol(syntax.FnName) ?? throw new ParseException($"找不到调用函数 {syntax.FnName}", syntax.Address);
        var function = symbol as FunctionSymbol;
        var boundArguments = ImmutableArray.CreateBuilder<BoundExpr>();

        foreach (var argument in syntax.Args)
        {
            var boundArgument = BindExpression(argument);
            boundArguments.Add(boundArgument);
        }
        if (function.Paramters != syntax.Args.Length) throw new ParseException($"函数调用参数不匹配 {syntax.FnName}", syntax.Address);
        return new BoundCallStatement(syntax, function, boundArguments.ToImmutable());
    }

    private BoundKeyActStatement BindGamepadActionStatement(KeyAction syntax)
    {
        switch(syntax)
        {
        }
        throw new NotImplementedException();
    }

    private BoundExpr BindExpression(ExprBase syntax)
    {
        return syntax switch
        {
            LiteralExpr lite => new BoundLiteralExpression(lite, lite.Value),
            VariableExpr => BindVarExpression((VariableExpr)syntax),
            ExtVarExpr exv => new BoundExternalVariableExpression(exv, exv.Var),
            UnaryExpression => throw new NotImplementedException(),
            BinaryExpression => BindBinaryExpression((BinaryExpression)syntax),
            ParenthesizedExpression pre => BindExpression(pre.Expression),
            _ => throw new Exception($"未知的表达式"),
        };
    }

    private BoundVariableExpression BindVarExpression(VariableExpr syntax)
    {
        var variable = BindVariableReference(syntax.Tag) ?? throw new Exception($"找不到变量 {syntax.Tag}");
        return new BoundVariableExpression(syntax, variable);
    }

    private BoundBinaryExpression BindBinaryExpression(BinaryExpression syntax)
    {
        var boundLeft = BindExpression(syntax.ValueLeft);
        var boundRight = BindExpression(syntax.ValueRight);

        var boundOperator = BoundBinaryOperator.Bind(syntax.Operator.Type, boundLeft.Type, boundRight.Type)
            ?? throw new Exception($"不支持的运算符:{syntax.Operator.Value}");
        return new BoundBinaryExpression(syntax, boundLeft, boundOperator, boundRight);
    }

    private VariableSymbol? BindVariableReference(string name)
    {
        return _scope.TryLookupSymbol(name) switch
        {
            VariableSymbol variable => variable,
            _ => null,
        };
    }
}
