using EasyCon.Script.Parsing;
using EasyCon.Script2;
using EasyCon.Script2.Symbols;
using System.Collections.Immutable;
using static EasyCon.Script.Binding.BoundFactory;

namespace EasyCon.Script.Binding;

internal sealed class Binder
{
    private readonly DiagnosticBag _diagnostics = [];
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
            foreach (var p in function.Paramters)
                _scope.TryDeclareVariable(p);
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

        foreach (var function in binder._scope.GetDeclaredFunctions())
        {
            var binderFn = new Binder(binder._scope, function);
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

        var main = new FunctionSymbol("$eval", [], ValueType.Void);
        var body = new BoundBlockStatement(null, statements.ToImmutable());
        var loweredBody = Lowerer.Flatten(body);
        functionBodies.Add(main, loweredBody);
        return new BoundProgram(main, functionBodies.ToImmutable());
    }

    private void BindFuncDeclaration(FuncDeclBlock syntax)
    {
        var parameters = ImmutableArray.CreateBuilder<ParamSymbol>();

        var seenParameterNames = new HashSet<string>();

        foreach (var parameterSyntax in syntax.Declare.Paramters)
        {
            var parameterName = parameterSyntax.Tag;
            var parameterType = ValueType.Int;
            if (!seenParameterNames.Add(parameterName))
            {
                throw new Exception($"重复定义的参数名 {parameterName}");
                //_diagnostics.ReportParameterAlreadyDeclared(parameterSyntax.Location, parameterName);
            }
            else
            {
                var parameter = new ParamSymbol(parameterName, parameterType, parameters.Count);
                parameters.Add(parameter);
            }
        }

        var function = new FunctionSymbol(syntax.Declare.Name, parameters.ToImmutable(), ValueType.Int, syntax);
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
                EmptyStmt => new BoundNop(syntax),
                IfBlock => BindIf((IfBlock)syntax),
                ForBlock => BindFor((ForBlock)syntax),
                AssignmentStmt => BindAssignStatement((AssignmentStmt)syntax),
                Break => BindBreakStatement((Break)syntax),
                Continue => BindContinueStatement((Continue)syntax),
                ReturnStmt => new BoundReturnStatement(syntax),
                CallStmt => BindCallStatement((CallStmt)syntax),
                KeyActionStmt => BindGamepadActionStatement((KeyActionStmt)syntax),
                Wait => BindWaitStatement((Wait)syntax),
                SerialPrint => throw new NotImplementedException(),
                _ => throw new Exception($"未知的语句类型{syntax}"),
            };
        }
        catch (Exception ex)
        {
            if (ex is ParseException) throw;
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
            GotoFalse(syntax, nextLabel, BindConversion(syntax.Condition.Condition, ValueType.Bool))
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
            block.Add(GotoFalse(syntax, elifLabel, BindConversion(elifCond.Condition, ValueType.Bool)));

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
        var forCond = syntax.Condition;
        var lowerBound = BindConversion(forCond.Lower, ValueType.Int);
        var upperBound = BindConversion(forCond.Upper, ValueType.Int);

        _scope = new BoundScope(_scope);

        var idxVar = syntax.Condition switch
        {
            For_Full ff => ff.RegIter,
            For_Static => new VariableExpr(Guid.NewGuid().ToString(), true),
            _ => null,
        };
        var variable = idxVar switch
        {
            VariableExpr v => BindVariableDeclaration(v, isReadOnly: true, ValueType.Int, allowGlobal: false),
            _ => null,
        };

        //var <= upperBound
        BoundExpr condition = syntax.Condition switch
        {
            For_Full or For_Static => Less(forCond.Upper, BindVarExpression(idxVar!), upperBound),
            _ => Literal(forCond.Upper, true),
        };

        BoundStmt lowerBoundStmt = variable == null ? Nop(syntax.Condition) :
            VariableDeclaration(syntax.Condition, variable, lowerBound);
        BoundStmt upperBoundStmt = variable == null ? Nop(syntax.Condition) :
            ConstantDeclaration(syntax.Condition, "upperBound", upperBound);

        // var+=step
        var step = Literal(forCond.Upper, 1);
        BoundStmt stepStmt = variable == null ? Nop(syntax.Condition) :
            VariableDeclaration(syntax, variable, Add(forCond.Upper, BindVarExpression(idxVar!), step));

        var body = BindLoopBody(syntax, syntax.Statements, out var breakLabel, out var continueLabel);
        _scope = _scope.Parent!;

        var lowwhile = new BoundWhileStatement(syntax,
             condition,
             Block(syntax,
                body,
                Label(syntax, continueLabel),
                stepStmt
                ),
             breakLabel,
             new BoundLabel($"label{++_labelCounter}")
             );

        return Block(syntax,
            lowerBoundStmt,
            upperBoundStmt,
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
        var level = (int)syntax.Level;
        if(level > 3)throw new ParseException("循环层数过多，请优化脚本", syntax.Address);
        if (_loopStack.Count < level)throw new ParseException("循环层数不足", syntax.Address);

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

    private BoundExprStatement BindAssignStatement(AssignmentStmt syntax)
    {
        var boundexpr = BindExpression(syntax.Expression);

        if (syntax.AugOp != null)
        {
            var desvar = BindVarExpression(syntax.DestVariable);
            var op = BoundBinaryOperator.Bind(syntax.AugOp.Operator);
            boundexpr = new BoundBinaryExpression(syntax.Expression, desvar, op!, boundexpr);
        }
        var variable = BindVariableDeclaration(syntax.DestVariable, syntax.DestVariable.ReadOnly, boundexpr.Type);

        if (variable.Type != boundexpr.Type) throw new ParseException("表达式和变量类型不匹配", syntax.Address);
        return new BoundExprStatement(syntax, new BoundAssignExpression(syntax.Expression, variable, boundexpr));
    }

    private VariableSymbol BindVariableDeclaration(VariableExpr syntax, bool isReadOnly, ValueType type, bool allowGlobal = true)
    {
        var vrr = _scope.TryLookupVar(syntax.Tag);
        if (vrr is not null)
        {
            if (vrr.IsReadOnly) throw new Exception($"只读变量无法修改：{syntax.Tag}");
            return vrr;
        }
        var variable = _function == null || allowGlobal
                    ? (VariableSymbol)new GlobalVariableSymbol(syntax.Tag, isReadOnly, type)
                    : new LocalVariableSymbol(syntax.Tag, isReadOnly, type);

        _scope.TryDeclareVariable(variable);

        return variable;
    }

    private BoundExprStatement BindCallStatement(CallStmt syntax)
    {
        // 绑定调用
        var expr = BindCallExpression(null, syntax.FnName, [.. syntax.Args]);

        return new BoundExprStatement(syntax, expr);
    }

    private BoundExprStatement BindWaitStatement(Wait syntax)
    {
        var boundArgument = BindExpression(syntax.Duration);
        boundArgument = BindConversion(boundArgument, ValueType.Int);

        var expr = new BoundCallExpression(null, BuiltinFunctions.Wait, [boundArgument]);
        return new BoundExprStatement(syntax, expr);
    }

    private BoundStmt BindGamepadActionStatement(KeyActionStmt syntax)
    {
        if(syntax is StickActionStmt st)
        {
            NSKeys.GetXYFromDegree(st.Degree, out byte x, out byte y);
            if (syntax is IDurationKey isk)
            {
                var dur = BindExpression(isk.Duration);
                return new BoundStickPressStatement(syntax, syntax.Key, dur, x, y);
            }
            else
            {
                return new BoundStickActStatement(syntax, syntax.Key, x, y);
            }
        }
        if (syntax is IDurationKey ikp)
        {
            var dur = BindExpression(ikp.Duration);
            return new BoundKeyPressStatement(syntax, syntax.Key, dur);
        }
        else
        {
            return new BoundKeyActStatement(syntax, syntax.Key, syntax.Up);
        }
    }

    private BoundExpr BindExpression(ExprBase syntax)
    {
        return syntax switch
        {
            LiteralExpr => BindLiterExpression((LiteralExpr)syntax),
            VariableExpr => BindVarExpression((VariableExpr)syntax),
            ExtVarExpr exv => new BoundExternalVariableExpression(exv, exv.Var),
            UnaryExpression => BindUnaryExpression((UnaryExpression)syntax),
            BinaryExpression => BindBinaryExpression((BinaryExpression)syntax),
            ParenthesizedExpression pre => BindExpression(pre.Expression),
            Callv1Expression => BindCallExpression((Callv1Expression)syntax),
            _ => throw new Exception($"未知的表达式"),
        };
    }

    private BoundLiteralExpression BindLiterExpression(LiteralExpr syntax)
    {
        return new BoundLiteralExpression(syntax, syntax.Value);
    }

    private BoundExpr BindConversion(BoundExpr expr, ValueType type)
    {
        if (type == ValueType.String) return new BoundConversionExpression(expr.Syntax, type, expr);
        if (expr.Type != type) throw new Exception("参数类型不匹配");
        return expr;
    }

    private BoundExpr BindConversion(ExprBase syntax, ValueType type)
    {
        var expr = BindExpression(syntax);
        if (expr.Type != type) throw new Exception("参数类型不匹配");
        return expr;
    }

    private BoundVariableExpression BindVarExpression(VariableExpr syntax)
    {
        var variable = _scope.TryLookupVar(syntax.Tag) switch
        {
            VariableSymbol v => v,
            _ => null,
        } ?? throw new Exception($"找不到变量 {syntax.Tag}");
        return new BoundVariableExpression(syntax, variable);
    }

    private BoundUnaryExpression BindUnaryExpression(UnaryExpression syntax)
    {
        var boundOperand = BindExpression(syntax.Operand);

        var boundOperator = BoundUnaryOperator.Bind(syntax.Operator.Type, boundOperand.Type)
            ?? throw new Exception($"不支持的运算符:{syntax.Operator.Value}");

        return new BoundUnaryExpression(syntax, boundOperator, boundOperand);
    }

    private BoundExpr BindBinaryExpression(BinaryExpression syntax)
    {
        var boundLeft = BindExpression(syntax.ValueLeft);
        var boundRight = BindExpression(syntax.ValueRight);

        var boundOperator = BoundBinaryOperator.Bind(syntax.Operator.Type, boundLeft.Type, boundRight.Type)
            ?? throw new Exception($"不支持的运算符:{syntax.Operator.Value}");

        if (boundLeft.ConstantValue != null && boundRight.ConstantValue != null)
        {
            var COS = boundOperator.Operate(boundLeft.ConstantValue, boundRight.ConstantValue);
            return new BoundLiteralExpression(syntax, COS);
        }

        return new BoundBinaryExpression(syntax, boundLeft, boundOperator, boundRight);
    }

    private BoundCallExpression BindCallExpression(Callv1Expression syntax, string fnName, ImmutableArray<ExprBase> Arguments)
    {
        var function = _scope.TryLookupFunc(fnName) ?? throw new Exception($"找不到调用函数 {fnName}");
        var boundArguments = ImmutableArray.CreateBuilder<BoundExpr>();

        foreach (var argument in Arguments)
        {
            var boundArgument = BindExpression(argument);
            boundArguments.Add(boundArgument);
        }
        if (Arguments.Length != function.Paramters.Length) throw new Exception($"函数调用参数不匹配 {fnName}");
        for (var i = 0; i < Arguments.Length; i++)
        {
            boundArguments[i] = BindConversion(boundArguments[i], function.Paramters[i].Type);
        }

        return new BoundCallExpression(syntax, function, boundArguments.ToImmutable());
    }

    private BoundCallExpression BindCallExpression(Callv1Expression syntax)
    {
        return BindCallExpression(syntax, syntax.Identifier.Value, syntax.Arguments);
    }

}
