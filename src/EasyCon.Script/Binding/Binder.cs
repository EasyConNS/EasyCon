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

        var main = new FunctionSymbol("$eval", []);
        var body = new BoundBlockStatement(null, statements.ToImmutable());
        var loweredBody = Lowerer.Flatten(body);
        functionBodies.Add(main, loweredBody);
        return new BoundProgram(main, functionBodies.ToImmutable());
    }

    private void BindFuncDeclaration(FuncDeclBlock syntax)
    {
        var function = new FunctionSymbol(syntax.Declare.Name, [], syntax);
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
            KeyAction => BindGamepadActionStatement((KeyAction)syntax),
            Wait => BindWaitStatement((Wait)syntax),
            SerialPrint => throw new NotImplementedException(),
            _ => throw new Exception($"未知的语句类型{syntax}"),
        };
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
        var lowerBound = BindConversion(syntax.Condition.Lower, ValueType.Int);
        var upperBound = BindConversion(syntax.Condition.Upper, ValueType.Int);

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
            For_Full or For_Static => Less(syntax.Condition.Upper, BindVarExpression(idxVar), upperBound),
            _ => new BoundLiteralExpression(syntax.Condition.Upper, true),
        };

        BoundStmt lowerBoundStmt = variable == null ? new BoundNop(syntax.Condition) :
            VariableDeclaration(syntax.Condition, variable, lowerBound);
        BoundStmt upperBoundStmt = variable == null ? new BoundNop(syntax.Condition) :
            ConstantDeclaration(syntax.Condition, "upperBound", upperBound);

        // var+=step
        var step = 1;
        BoundStmt stepStmt = variable == null ? new BoundNop(syntax.Condition) :
            new BoundAssignStatement(syntax, variable, Add(syntax.Condition.Upper, BindVarExpression(idxVar), Literal(syntax.Condition.Upper, step)));

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
        var levelExp = BindExpression(syntax.Level) as BoundLiteralExpression ?? throw new Exception("break层数必须是数字");
        if (levelExp.Type != ValueType.Int) throw new Exception("break层数必须是数字");
        var level = (int)levelExp.ConstantValue;
        if (_loopStack.Count < level)
        {
            throw new Exception("循环层数不足");
        }

        var breakLabel = _loopStack.ElementAt(level - 1).BreakLabel;
        // var breakLabel = _loopStack.Peek().BreakLabel;
        return new BoundGotoStatement(syntax, breakLabel);
    }

    private BoundGotoStatement BindContinueStatement(Continue syntax)
    {
        if (_loopStack.Count == 0)
        {
            throw new Exception("循环层数不足");
        }

        var continueLabel = _loopStack.Peek().ContinueLabel;
        return new BoundGotoStatement(syntax, continueLabel);
    }

    private BoundAssignStatement BindAssignStatement(AssignmentStmt syntax)
    {
        var boundexpr = BindExpression(syntax.Expression);

        if (syntax.AugOp != null)
        {
            var desvar = BindVarExpression(syntax.DestVariable);
            var op = BoundBinaryOperator.Bind(syntax.AugOp.Operator);
            boundexpr = new BoundBinaryExpression(syntax.Expression, desvar, op!, boundexpr);
        }
        var variable = BindVariableDeclaration(syntax.DestVariable, syntax.DestVariable.ReadOnly, boundexpr.Type);

        if (variable.Type != boundexpr.Type) throw new Exception("表达式和变量类型不匹配");

        return new BoundAssignStatement(syntax, variable, boundexpr);
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

    private BoundStmt BindCallStatement(CallStmt syntax)
    {
        var function = _scope.TryLookupFunc(syntax.FnName) ?? throw new Exception($"找不到调用函数 {syntax.FnName}");
        var boundArguments = ImmutableArray.CreateBuilder<BoundExpr>();

        // 特殊处理
        if (BuiltinFunctions.GetAll().Any(f => f == function && f == BuiltinFunctions.Timestamp))
        {
            var des = BindVariableDeclaration((VariableExpr)syntax.Args[0], isReadOnly: false, ValueType.Int);
            return new BoundAssignStatement(syntax, des, new BoundCallExpression(null, function, []));
        }
        foreach (var argument in syntax.Args)
        {
            var boundArgument = BindExpression(argument);
            boundArguments.Add(boundArgument);
        }
        if (syntax.Args.Length != function.Paramters.Length) throw new Exception($"函数调用参数不匹配 {syntax.FnName}");
        for (var i = 0; i < syntax.Args.Length; i++)
        {
            boundArguments[i] = BindConversion(boundArguments[i], function.Paramters[i]);
        }

        var expr = new BoundCallExpression(null, function, boundArguments.ToImmutable());
        // 特殊处理
        if (BuiltinFunctions.GetAll().Any(f => f == function && f == BuiltinFunctions.Rand))
        {
            var des = BindVariableDeclaration((VariableExpr)syntax.Args[0], isReadOnly: false, ValueType.Int);
            return new BoundAssignStatement(syntax, des, expr);
        }
        return new BoundCallStatement(syntax, expr);
    }

    private BoundCallStatement BindWaitStatement(Wait syntax)
    {
        var boundArgument = BindExpression(syntax.Duration);
        boundArgument = BindConversion(boundArgument, ValueType.Int);

        var expr = new BoundCallExpression(null, BuiltinFunctions.Wait, [boundArgument]);
        return new BoundCallStatement(syntax, expr);
    }

    private BoundKeyActStatement BindGamepadActionStatement(KeyAction syntax)
    {
        if (syntax is KeyPress kp)
        {
            var dur = BindExpression(kp.Duration);
            return new BoundKeyPressStatement(syntax, kp.Key, dur);
        }
        if (syntax is StickPress sp)
        {
            var dur = BindExpression(sp.Duration);
            return new BoundKeyPressStatement(syntax, sp.Key, dur);
        }
        var up = (syntax is KeyUp) || (syntax is StickUp);
        return new BoundKeyActStatement(syntax, syntax.Key, up);
    }

    private BoundExpr BindExpression(ExprBase syntax)
    {
        return syntax switch
        {
            LiteralExpr lite => new BoundLiteralExpression(lite, lite.Value),
            VariableExpr => BindVarExpression((VariableExpr)syntax),
            ExtVarExpr exv => new BoundExternalVariableExpression(exv, exv.Var),
            UnaryExpression => BindUnaryExpression((UnaryExpression)syntax),
            BinaryExpression => BindBinaryExpression((BinaryExpression)syntax),
            ParenthesizedExpression pre => BindExpression(pre.Expression),
            _ => throw new Exception($"未知的表达式"),
        };
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
        } ?? throw new ParseException($"找不到变量 {syntax.Tag}");
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

        if( boundLeft.ConstantValue != null && boundRight.ConstantValue != null )
        {
            var COS = boundOperator.Operate(boundLeft.ConstantValue, boundRight.ConstantValue);
            return new BoundLiteralExpression(syntax, COS);
        }

        return new BoundBinaryExpression(syntax, boundLeft, boundOperator, boundRight);
    }
}
