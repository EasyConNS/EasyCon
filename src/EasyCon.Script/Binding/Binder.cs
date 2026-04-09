using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using System.Collections.Immutable;
using static EasyCon.Script.Binding.BoundFactory;

namespace EasyCon.Script.Binding;

internal sealed class Binder
{
    private readonly DiagnosticBag _diagnostics = [];
    private readonly FunctionSymbol? _function;

    private readonly Stack<(BoundLabel BreakLabel, BoundLabel ContinueLabel)> _loopStack = new();
    private int _labelCounter = 0;
    private BoundScope _scope;
    private readonly HashSet<string> _ilNames = [];
    private HashSet<string> _validExternalVariables = [];

    public DiagnosticBag Diagnostics => _diagnostics;

    private Binder(BoundScope? parent, FunctionSymbol? function)
    {
        _scope = new BoundScope(parent);
        _function = function;

        if (function != null)
        {
            foreach (var p in function.Parameters)
                _scope.TryDeclareVariable(p);
        }
    }

    void SetValidExternalVariables(IEnumerable<string> validNames)
    {
        _validExternalVariables = [.. validNames];
    }

    public static BoundProgram BindProgram(SyntaxTree syntaxs, IEnumerable<string>? externalVariables = null)
    {
        var parentScope = CreateRootScope();
        var binder = new Binder(parentScope, function: null);
        binder.Diagnostics.AddRange(syntaxs.Diagnostics);
        binder.SetValidExternalVariables(externalVariables ?? []);

        var functionBodies = ImmutableDictionary.CreateBuilder<FunctionSymbol, BoundBlockStatement>();

        var functionDeclarations = syntaxs.FlattenRoot.SelectMany(st => st.Members).OfType<FuncDeclBlock>();

        foreach (var function in functionDeclarations)
        {
            binder.BindFuncDeclaration(function);
        }

        var firstGlobalStatementPerSyntaxTree = syntaxs.FlattenRoot.Select(st => st.Members.Where(m => m is not FuncDeclBlock && m is not EmptyStmt).FirstOrDefault())
                                                                .Where(g => g != null)
                                                                .ToArray();
        if (firstGlobalStatementPerSyntaxTree.Length > 1)
            throw new ParseException("脚本主语句只能存在一个文件中", firstGlobalStatementPerSyntaxTree.First()!.Address);

        var globalStatements = syntaxs.FlattenRoot.SelectMany(st => st.Members)
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
            var fnloweredBody = Lowerer.Lower(function, fnbody);

            if (function.ReturnType != ScriptType.Void && !ControlFlowGraph.AllPathsReturn(fnloweredBody))
                throw new ParseException("函数所有路径必须有返回值", function.Declaration.Address);

            binder._ilNames.UnionWith(binderFn._ilNames);

            functionBodies.Add(function, fnloweredBody);
        }

        var main = new FunctionSymbol("$eval", [], [], ScriptType.Void);
        var body = new BoundBlockStatement(null, statements.ToImmutable());
        var loweredBody = Lowerer.Lower(main, body);
        functionBodies.Add(main, loweredBody);
        return new BoundProgram(main, functionBodies.ToImmutable(), binder._ilNames.ToImmutableArray());
    }

    private void BindFuncDeclaration(FuncDeclBlock syntax)
    {
        var parameters = ImmutableArray.CreateBuilder<ParamSymbol>();
        var seenParameterNames = new HashSet<string>();

        for (int i = 0; i < syntax.Declare.Paramters.Length; i++)
        {
            var parameterSyntax = syntax.Declare.Paramters[i];
            var parameterName = parameterSyntax.Identifier.Tag;
            var parameterType = BindTypeClause(syntax.Declare, parameterSyntax.Type)?? ScriptType.Int;

            if (!seenParameterNames.Add(parameterName))
                throw new ParseException($"重复定义的参数名 {parameterName}", syntax.Declare.Address);

            // 检查是否是最后一个参数且带有默认值
            bool isLast = i == syntax.Declare.Paramters.Length - 1;

            // 如果不是最后一个参数却尝试定义默认值，抛出异常
             //if (!isLast && parameterSyntax.) throw new ParseException("只有最后一个参数可以有默认值", syntax.Declare.Address);

            var parameter = new ParamSymbol(parameterName, parameterType, parameters.Count);
            parameters.Add(parameter);
        }

        var returnType = BindTypeClause(syntax?.Declare) ?? ScriptType.Void;
        var function = new FunctionSymbol(syntax.Declare.Name, [], parameters.ToImmutable(), returnType, syntax);
        _scope.TryDeclareFunction(function);
    }

    private static BoundScope CreateRootScope()
    {
        var result = new BoundScope(null);

        foreach (var f in BuiltinFunctions.GetAll())
            result.TryDeclareFunction(f);

        return result;
    }
    #region 核心泛型推导逻辑

    private (ScriptType[] BoundParams, ScriptType BoundReturn) BindGenericFunction(FunctionSymbol function, ImmutableArray<BoundExpr> arguments)
    {
        // 1. 如果不是泛型函数，直接返回原签名
        if (function.TypeParameters.IsEmpty)
        {
            return (function.Parameters.Select(p => p.Type).ToArray(), function.ReturnType);
        }

        // 2. 建立类型映射表 (例如 T -> int)
        var substitution = new Dictionary<TypeParameter, ScriptType>();

        for (int i = 0; i < function.Parameters.Length; i++)
        {
            InferTypeParameters(function.Parameters[i].Type, arguments[i].Type, substitution);
        }

        // 3. 验证推导完整性
        foreach (var tp in function.TypeParameters)
        {
            if (!substitution.ContainsKey(tp))
                throw new Exception($"无法为函数 {function.Name} 推导泛型参数 {tp.Name}");
        }

        // 4. 生成绑定后的具体类型
        var boundParams = function.Parameters.Select(p => SubstituteType(p.Type, substitution)).ToArray();
        var boundReturn = SubstituteType(function.ReturnType, substitution);

        return (boundParams, boundReturn);
    }

    private void InferTypeParameters(ScriptType formal, ScriptType actual, Dictionary<TypeParameter, ScriptType> sub)
    {
        if (formal is TypeParameter tp)
        {
            if (sub.TryGetValue(tp, out var existing))
            {
                if (!existing.Equals(actual))
                    throw new Exception($"泛型冲突：{tp.Name} 同时被推导为 {existing} 和 {actual}");
            }
            else sub[tp] = actual;
        }
        else if (formal is GenericType fGen && actual is GenericType aGen)
        {
            if (fGen.Definition.Name != aGen.Definition.Name) return;
            for (int i = 0; i < fGen.TypeArguments.Length; i++)
                InferTypeParameters(fGen.TypeArguments[i], aGen.TypeArguments[i], sub);
        }
    }

    private ScriptType SubstituteType(ScriptType type, Dictionary<TypeParameter, ScriptType> sub)
    {
        if (type is TypeParameter tp) return sub.GetValueOrDefault(tp, type);
        if (type is GenericType gt)
        {
            return gt.Definition.Bind([.. gt.TypeArguments.Select(t => SubstituteType(t, sub))]);
        }
        return type;
    }

    #endregion
    private BoundStmt BindStatement(Statement syntax)
    {
        try
        {
            return syntax switch
            {
                EmptyStmt or ImportStmt => new BoundNop(syntax),
                ConstantDeclStmt => BindConstantDeclaration((ConstantDeclStmt)syntax),
                AssignmentStmt => BindAssignStatement((AssignmentStmt)syntax),
                IfBlock => BindIf((IfBlock)syntax),
                ForBlock => BindFor((ForBlock)syntax),
                WhileBlock => BindWhile((WhileBlock)syntax),
                Break => BindBreakStatement((Break)syntax),
                Continue => BindContinueStatement((Continue)syntax),
                ReturnStmt => BindReturnStatement((ReturnStmt)syntax),
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

    private BoundStmt BindBlockStatement(Statement syntax, ImmutableArray<Statement> body)
    {
        var statements = ImmutableArray.CreateBuilder<BoundStmt>();
        _scope = new BoundScope(_scope);

        foreach (var statementSyntax in body)
        {
            var statement = BindStatement(statementSyntax);
            statements.Add(statement);
        }

        _scope = _scope.Parent!;

        return new BoundBlockStatement(syntax, statements.ToImmutable());
    }

    private BoundBlockStatement BindIf(IfBlock syntax)
    {
        _labelCounter++;
        var endLabel = new BoundLabel($"IfEnd_{_labelCounter}");
        var nextLabel = new BoundLabel($"NEXT_{_labelCounter}");

        var block = new List<BoundStmt>
        {
            GotoFalse(syntax, nextLabel, BindConversion(syntax.Condition.Condition, ScriptType.Bool))
        };
        static bool isCtrl(Statement st)
        {
            if (st is ElseIf || st is Else || st is EndIf) return true;
            return false;
        }
        var index = 0;
        _scope = new BoundScope(_scope);
        while (index < syntax.Statements.Length && !isCtrl(syntax.Statements[index]))
        {
            block.Add(BindStatement(syntax.Statements[index]));
            index++;
        }
        _scope = _scope.Parent!;
        block.Add(Goto(syntax, endLabel));
        block.Add(Label(syntax, nextLabel));
        // 处理 elif 分支
        int elifCount = 0;
        while (index < syntax.Statements.Length && syntax.Statements[index] is ElseIf elifCond)
        {
            var elifLabel = new BoundLabel($"ELIF_{_labelCounter}_{elifCount}");
            block.Add(GotoFalse(syntax, elifLabel, BindConversion(elifCond.Condition, ScriptType.Bool)));

            index++;

            _scope = new BoundScope(_scope);
            while (index < syntax.Statements.Length && !isCtrl(syntax.Statements[index]))
            {
                block.Add(BindStatement(syntax.Statements[index]));
                index++;
            }
            _scope = _scope.Parent!;

            block.Add(Goto(syntax, endLabel));
            block.Add(Label(syntax, elifLabel));
            elifCount++;
        }
        // 处理 else 分支
        if (index < syntax.Statements.Length && syntax.Statements[index] is Else)
        {
            index++;
            _scope = new BoundScope(_scope);
            while (index < syntax.Statements.Length && !isCtrl(syntax.Statements[index]))
            {
                block.Add(BindStatement(syntax.Statements[index]));
                index++;
            }
            _scope = _scope.Parent!;
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
        var lowerBound = BindConversion(forCond.Lower, ScriptType.Int);
        var upperBound = BindConversion(forCond.Upper, ScriptType.Int);

        _scope = new BoundScope(_scope);

        var idxVar = forCond switch
        {
            For_Full ff => ff.RegIter,
            For_Static => new VariableExpr($"_tmpL${_labelCounter++}", true),
            _ => null,
        };
        var variable = idxVar switch
        {
            VariableExpr v => BindVariableDeclaration(v, isReadOnly: true, ScriptType.Int, allowGlobal: false),
            _ => null,
        };

        //      var <var> = <lower>
        BoundStmt lowerBoundStmt = variable == null ? Nop(forCond) :
            VariableDeclaration(forCond, variable, lowerBound);
        //      let upperBound = <upper>
        BoundStmt upperBoundStmt = variable == null ? Nop(forCond) :
            ConstantDeclaration(forCond, "_uppBound$", upperBound);
        //var <= upperBound
        switch(forCond.Upper)
        {
            case LiteralExpr litE:
                upperBoundStmt = Nop(forCond);
                break;
            case VariableExpr varE:
                upperBound = Variable(forCond.Upper, ((BoundVariableDeclaration)upperBoundStmt).Variable);
                break;
        }
        BoundExpr condition = forCond switch
        {
            For_Full or For_Static => LessEqual(forCond.Upper, Variable(forCond.Lower, variable), upperBound),
            _ => Literal(forCond.Upper, true),
        };
        // var+=step
        var step = Literal(forCond.Upper, 1);
        BoundStmt stepStmt = variable == null ? Nop(forCond) :
            VariableDeclaration(syntax, variable, Add(forCond.Upper, Variable(forCond.Lower, variable), step));

        var body = BindLoopBody(syntax, syntax.Statements, out var breakLabel, out var continueLabel);
        _scope = _scope.Parent!;
        // for <var> = <lower> to <upper>
        //      <body>
        //
        // ---->
        //
        // {
        //      var <var> = <lower>
        //      let upperBound = <upper>
        //      while (<var> <= upperBound)
        //      {
        //          <body>
        //          continue:
        //          <var> = <var> + 1
        //      }
        // }
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
            RewriteWhile(lowwhile)
             );
    }

    private BoundBlockStatement BindWhile(WhileBlock syntax)
    {
        var body = BindLoopBody(syntax, syntax.Statements, out var breakLabel, out var continueLabel);

        var lowwhile = new BoundWhileStatement(syntax,
            BindConversion(syntax.Condition.Condition, ScriptType.Bool),
            body,
            breakLabel,
            continueLabel
             );
        return RewriteWhile(lowwhile);
    }

    private BoundBlockStatement RewriteWhile(BoundWhileStatement boundsyntax)
    {
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

    private BoundReturnStatement BindReturnStatement(ReturnStmt syntax)
    {
        var expression = syntax.Expression == null ? null : BindExpression(syntax.Expression);

        if (_function != null)
        {
            if (_function.ReturnType == ScriptType.Void)
            {
                if (expression != null)
                    throw new ParseException($"函数 '{_function.Name}' 返回类型为 void，不应有返回值", syntax.Address);
            }
            else
            {
                if (expression == null)
                    throw new ParseException($"函数 '{_function.Name}' 必须返回类型为 {_function.ReturnType} 的值", syntax.Address);

                // 关键修改：检查返回值是否与函数定义的返回类型兼容
                expression = BindConversion(expression, _function.ReturnType, "返回值类型不匹配");
            }
        }
        return new BoundReturnStatement(syntax, expression);
    }

    private BoundStmt BindConstantDeclaration(ConstantDeclStmt syntax)
    {
        var boundexpr = BindExpression(syntax.Expression);
        if (boundexpr.ConstantValue == Value.Void) throw new ParseException("常量表达式不正确", syntax.Address);

        // Check if constant already exists
        var existingVar = _scope.TryLookupVar(syntax.Constant.Tag);
        if (existingVar != null)
        {
            throw new ParseException($"常量 '{syntax.Constant.Tag}' 已经定义", syntax.Address);
        }

        // Constants are always read-only
        var variable = LookupVariable(syntax.Constant, true, boundexpr.Type);

        if (!variable.Type.IsAssignableFrom(boundexpr.Type))
            throw new ParseException($"类型不匹配：无法将 {boundexpr.Type} 赋值给常量 {variable.Type}", syntax.Address);

        return new BoundVariableDeclaration(syntax, variable, boundexpr);
    }

    private BoundVariableDeclaration BindAssignStatement(AssignmentStmt syntax)
    {
        var boundexpr = BindExpression(syntax.Expression);

        if (syntax.AssignmentToken.Value != "=")
        {
            // a <op>= b
            //
            // ---->
            //
            // a = (a <op> b)
            var desvar = BindVarExpression(syntax.DestVariable);
            var op = BoundBinaryOperator.Bind(syntax.AssignmentToken.Type, desvar.Type, boundexpr.Type)
                ?? throw new Exception($"不支持的运算符:{syntax.AssignmentToken.Value}对于类型 <{desvar.Type}>和<{boundexpr.Type}> ");
            boundexpr = new BoundBinaryExpression(syntax.Expression, desvar, op!, boundexpr);
        }

        if (boundexpr.Type == ScriptType.Void) throw new ParseException("空值表达式无法赋值", syntax.Address);
        var variable = BindVariableDeclaration(syntax.DestVariable, syntax.DestVariable.ReadOnly, boundexpr.Type);

        if (!variable.Type.IsAssignableFrom(boundexpr.Type))
            throw new ParseException($"类型不匹配：无法将 {boundexpr.Type} 赋值给 {variable.Type}", syntax.Address);

        return new BoundVariableDeclaration(syntax, variable, boundexpr);
    }

    private VariableSymbol BindVariableDeclaration(VariableExpr syntax, bool isReadOnly, ScriptType type, bool allowGlobal = true)
    {
        var vrr = _scope.TryLookupVar(syntax.Tag);
        if (vrr is not null)
        {
            if (vrr.IsReadOnly) throw new Exception($"只读变量无法修改：{syntax.Tag}");
            return vrr;
        }

        return LookupVariable(syntax, isReadOnly, type, allowGlobal);
    }
    private VariableSymbol LookupVariable(VariableExpr syntax, bool isReadOnly, ScriptType type, bool allowGlobal = true)
    {
        var variable = _function == null && allowGlobal
                    ? (VariableSymbol)new GlobalVariableSymbol(syntax.Tag, isReadOnly, type)
                    : new LocalVariableSymbol(syntax.Tag, isReadOnly, type);

        _scope.TryDeclareVariable(variable);

        return variable;
    }
    private ScriptType? BindTypeClause(Statement syntax, TypeClauseSyntax? tcs)
    {
        if (tcs == null) return null;
        return LookupType(tcs.Identifier.Value) ?? throw new ParseException($"未知类型：{tcs.Identifier.Text}", syntax.Address);
    }

    private ScriptType? BindTypeClause(FuncStmt syntax)
    {
        if (syntax.Type == null) return null;
        return LookupType(syntax.Type.Identifier.Value) ?? throw new ParseException($"未知类型：{syntax.Identifier.Text}", syntax.Address);
    }

    private ScriptType? LookupType(string name)
    {
        var upper = name.ToUpper();
        if (upper.EndsWith("[]"))
        {
            var elem = LookupType(upper.Substring(0, upper.Length - 2));
            return elem == null ? null : ScriptType.Array.Bind(elem);
        }
        return upper switch
        {
            "BOOL" => ScriptType.Bool,
            "INT" => ScriptType.Int,
            "STRING" => ScriptType.String,
            _ => null,
        };
    }

    private BoundExprStatement BindCallStatement(CallStmt syntax)
    {
        var name = syntax.FnName;
        if(BuiltinFunctions.GetAll().Select(f => f.Name).Contains(syntax.FnName.ToUpper()))
        {
            name = syntax.FnName.ToUpper();
        }
        // 绑定调用
        var expr = BindCallExpression(null, name, [.. syntax.Args]);

        return new BoundExprStatement(syntax, expr);
    }

    private BoundExprStatement BindWaitStatement(Wait syntax)
    {
        var boundArgument = BindExpression(syntax.Duration);
        boundArgument = BindConversion(boundArgument, ScriptType.Int);

        var expr = new BoundCallExpression(null, BuiltinFunctions.Wait, [boundArgument], ScriptType.Void);
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
            ExtVarExpr => BindExtraLabel((ExtVarExpr)syntax),
            UnaryExpression => BindUnaryExpression((UnaryExpression)syntax),
            BinaryExpression => BindBinaryExpression((BinaryExpression)syntax),
            ParenthesizedExpression pre => BindExpression(pre.Expression),
            Callv1Expression => BindCallExpression((Callv1Expression)syntax),
            IndexDefExpression => BindIndexExpression((IndexDefExpression)syntax),
            IndexVisitExpression => BindIndexVisitExpression((IndexVisitExpression)syntax),
            SliceExpression => BoundSliceExpression((SliceExpression)syntax),
            _ => throw new Exception($"未知的表达式"),
        };
    }

    private BoundExternalVariableExpression BindExtraLabel(ExtVarExpr syntax)
    {
        var name = syntax.Name;

        // 在 binder 阶段验证外部变量名称
        if (!_validExternalVariables.Contains(name))
            throw new Exception("找不到识图标签\"@" + name + "\"");

        _ilNames.Add(name);
        return new BoundExternalVariableExpression(syntax, name);
    }

    private BoundLiteralExpression BindLiterExpression(LiteralExpr syntax)
    {
        var val = syntax.Value;
        if (syntax.Value is string input)
        {
            if (input.Length >= 2 && input[0] == input[^1])
            {
                if(input[0] == '"' || input[0] == '\'')
                    val = input[1..^1];
            }
        }
        Value obj = Value.From(val);
        return new BoundLiteralExpression(syntax, obj);
    }

    private BoundIndexDeclxpression BindIndexExpression(IndexDefExpression syntax)
    {
        var boundIndexs = ImmutableArray.CreateBuilder<BoundExpr>();

        foreach (var index in syntax.Index)
        {
            var boundIndex = BindExpression(index);
            boundIndexs.Add(boundIndex);
        }
        if (boundIndexs.Select(i=>i.Type).Distinct().Count() != 1) throw new Exception("数组成员类型必须一致");

        return new BoundIndexDeclxpression(syntax, boundIndexs.ToImmutable());
    }

    private BoundIndexVariableExpression BindIndexVisitExpression(IndexVisitExpression syntax)
    {
        var baseExpr = BindExpression(syntax.Var);

        // 1. 类型校验：仅支持 字符串 或 泛型数组
        bool isString = baseExpr.Type.Equals(ScriptType.String);
        bool isArray = baseExpr.Type is GenericType { Definition.Name: "Array" };

        if (!isString && !isArray)
        {
            throw new Exception($"类型 '{baseExpr.Type}' 不支持索引访问。");
        }

        // 2. 索引必须能转换为整数
        var indexExpr = BindConversion(syntax.Index, ScriptType.Int, "数组索引必须是整数类型");

        // 3. 确定结果类型
        ScriptType resultType;
        if (isString)
        {
            resultType = ScriptType.String;
        }
        else
        {
            // 从 Array<T> 中提取 T
            var genericType = (GenericType)baseExpr.Type;
            resultType = genericType.TypeArguments[0];
        }

        return new BoundIndexVariableExpression(syntax, baseExpr, indexExpr, resultType);
    }

    private BoundSliceExpression BoundSliceExpression(SliceExpression syntax)
    {
        var baseExpr = BindExpression(syntax.Var);

        // 1. 类型校验
        bool isString = baseExpr.Type.Equals(ScriptType.String);
        bool isArray = baseExpr.Type is GenericType { Definition.Name: "Array" };

        if (!isString && !isArray)
        {
            throw new Exception($"类型 '{baseExpr.Type}' 不支持切片操作 (Range Slice)。");
        }

        // 2. 绑定起始和结束索引，并确保它们是整数
        var startExpr = BindConversion(syntax.Start, ScriptType.Int, "切片起始位置必须是整数");
        var endExpr = BindConversion(syntax.End, ScriptType.Int, "切片结束位置必须是整数");

        // 3. 结果类型与基础表达式类型完全一致
        // String -> String, Array<int> -> Array<int>
        ScriptType resultType = baseExpr.Type;

        return new BoundSliceExpression(syntax, baseExpr, startExpr, endExpr, resultType);
    }

    private BoundExpr BindConversion(BoundExpr expr, ScriptType type, string msg = "表达式类型不匹配")
    {
        if (type.IsAssignableFrom(expr.Type)) return expr;
        if (type == ScriptType.String) return new BoundConversionExpression(expr.Syntax, type, expr);
        throw new Exception($"{msg}: 无法将 {expr.Type} 转换为 {type}");
    }

    private BoundExpr BindConversion(ExprBase syntax, ScriptType type, string msg = "表达式类型不匹配")
    {
        var expr = BindExpression(syntax);
        if (expr.Type != type) throw new Exception(msg);
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
            ?? throw new Exception($"不支持的运算符:{syntax.Operator.Value}对于类型 <{boundOperand.Type}>");

        return new BoundUnaryExpression(syntax, boundOperator, boundOperand);
    }

    private BoundExpr BindBinaryExpression(BinaryExpression syntax)
    {
        var boundLeft = BindExpression(syntax.ValueLeft);
        var boundRight = BindExpression(syntax.ValueRight);

        var boundOperator = BoundBinaryOperator.Bind(syntax.Operator.Type, boundLeft.Type, boundRight.Type)
            ?? throw new Exception($"不支持的运算符:{syntax.Operator.Value}对于类型 <{boundLeft.Type}>和<{boundRight.Type}> ");

        if (boundLeft.ConstantValue != Value.Void && boundRight.ConstantValue != Value.Void)
        {
            var COS = boundOperator.Operate(boundLeft.ConstantValue, boundRight.ConstantValue);
            return new BoundLiteralExpression(syntax, COS);
        }

        return new BoundBinaryExpression(syntax, boundLeft, boundOperator, boundRight);
    }

    private BoundCallExpression BindCallExpression(Callv1Expression syntax, string fnName, ImmutableArray<ExprBase> Arguments)
    {
        var function = _scope.TryLookupFunc(fnName) ?? throw new Exception($"找不到调用函数 {fnName}");

        // 1. 先绑定实参表达式
        var boundArgs = Arguments.Select(BindExpression).ToImmutableArray();

        // 2. 检查参数数量
        int minArgs = function.Parameters.Count(p => !p.HasDefaultValue);
        int maxArgs = function.Parameters.Length;

        if (boundArgs.Length < minArgs || boundArgs.Length > maxArgs)
            throw new Exception($"函数 {fnName} 参数数量不匹配");

        // 3. 泛型绑定与实例化
        var (instParams, instReturn) = BindGenericFunction(function, boundArgs);

        // 4. 类型转换与最终参数确定
        var finalArgs = ImmutableArray.CreateBuilder<BoundExpr>();
        for (int i = 0; i < function.Parameters.Length; i++)
        {
            var param = function.Parameters[i];

            if (i < boundArgs.Length)
            {
                finalArgs.Add(BindConversion(boundArgs[i], instParams[i]));
            }
            else if (param.HasDefaultValue)
            {
                // 补全默认值
                finalArgs.Add(new BoundLiteralExpression(syntax!, Value.From(param.DefaultValue)));
            }
        }

        return new BoundCallExpression(syntax, function, finalArgs.ToImmutable(), instReturn);
    }

    private BoundCallExpression BindCallExpression(Callv1Expression syntax)
    {
        return BindCallExpression(syntax, syntax.Identifier.Value, syntax.Arguments);
    }

}
