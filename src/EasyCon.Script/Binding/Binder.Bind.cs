using EasyCon.Script.Runtime;
using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using EasyCon.Script.Text;
using System.Collections.Immutable;
using System.Linq;
using static EasyCon.Script.Binding.BoundFactory;

namespace EasyCon.Script.Binding;

internal sealed partial class Binder
{
    #region 核心泛型推导逻辑

    private (ScriptType[] BoundParams, ScriptType BoundReturn) BindGenericFunction(AstNode syntax, FunctionSymbol function, ImmutableArray<BoundExpr> arguments)
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
            InferTypeParameters(syntax, function.Parameters[i].Type, arguments[i].Type, substitution);
        }

        // 3. 验证推导完整性
        foreach (var tp in function.TypeParameters)
        {
            if (!substitution.ContainsKey(tp))
            {
                _diagnostics.ReportGenericTypeInferenceFailed(syntax.Syntax.Location, function.Name, tp.Name);
                return ([ScriptType.Void], ScriptType.Void);
            }
        }

        // 4. 生成绑定后的具体类型
        var boundParams = function.Parameters.Select(p => SubstituteType(p.Type, substitution)).ToArray();
        var boundReturn = SubstituteType(function.ReturnType, substitution);

        return (boundParams, boundReturn);
    }

    private void InferTypeParameters(AstNode syntax, ScriptType formal, ScriptType actual, Dictionary<TypeParameter, ScriptType> sub)
    {
        if (formal is TypeParameter tp)
        {
            if (sub.TryGetValue(tp, out var existing))
            {
                if (!existing.Equals(actual))
                    _diagnostics.ReportGenericTypeConflict(syntax.Syntax.Location, tp.Name, existing, actual);
            }
            else sub[tp] = actual;
        }
        else if (formal is GenericType fGen && actual is GenericType aGen)
        {
            if (fGen.Definition.Name != aGen.Definition.Name) return;
            for (int i = 0; i < fGen.TypeArguments.Length; i++)
                InferTypeParameters(syntax, fGen.TypeArguments[i], aGen.TypeArguments[i], sub);
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
            var result = syntax switch
            {
                EmptyStmt or ImportStmt or StructDeclBlock => new BoundNop(syntax),
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
            if (result is BoundExprStatement es)
            {
                if (es.Expression is BoundErrorExpression)
                {
                    _diagnostics.ReportInvalidExpressionStatement(syntax.Location);
                }
            }
            return result;
        }
        catch (Exception ex)
        {
            _diagnostics.ReportBadStruct(syntax.Location, ex.Message);
            return BindErrorStatement(syntax);
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

    private BoundExprStatement BindErrorStatement(Statement syntax) => new(syntax, new BoundErrorExpression(syntax));

    private BoundBlockStatement BindIf(IfBlock syntax)
    {
        _labelCounter++;
        var endLabel = new BoundLabel($"IfEnd_{_labelCounter}");
        var nextLabel = new BoundLabel($"NEXT_{_labelCounter}");
        var block = ImmutableList.CreateBuilder<BoundStmt>();

        block.Add(GotoFalse(syntax, nextLabel, BindConversion(syntax.Condition.Condition, ScriptType.Bool)));
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
            For_Static => new VariableExpr(new(syntax.Syntax.Text, TokenType.VAR, $"_tmpL${_labelCounter++}", 0), true),
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
        switch (forCond.Upper)
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
            For_Full or For_Static => LessEqual(forCond.Upper, Variable(forCond.Lower, variable!), upperBound),
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

    private BoundStmt BindBreakStatement(Break syntax)
    {
        var level = (int)syntax.Level;
        if (level > _max_allow_level)
            _diagnostics.ReportTooMuchLoop(syntax.Syntax.Location);
        if (_loopStack.Count < level)
        {
            _diagnostics.ReportInvalidBreakOrContinue(syntax.Syntax.Location, syntax.Syntax);
            return BindErrorStatement(syntax);
        }

        var breakLabel = _loopStack.ElementAt(level - 1).BreakLabel;
        return new BoundGotoStatement(syntax, breakLabel);
    }

    private BoundStmt BindContinueStatement(Continue syntax)
    {
        if (_loopStack.Count == 0)
        {
            _diagnostics.ReportInvalidBreakOrContinue(syntax.Syntax.Location, syntax.Syntax);
            return BindErrorStatement(syntax);
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
                {
                    _diagnostics.ReportVoidFunctionCannotReturn(syntax.Location, _function, expression.Type);
                    expression = null;
                }
            }
            else
            {
                if (expression == null)
                {
                    _diagnostics.ReportFunctionMustReturnValue(syntax.Location, _function, _function.ReturnType);
                    expression = new BoundErrorExpression(syntax);
                }
                else
                {
                    // 关键修改：检查返回值是否与函数定义的返回类型兼容
                    expression = BindConversion(expression, _function.ReturnType);
                }
            }
        }

        var returnStatement = new BoundReturnStatement(syntax, expression);

        // 尾递归检测：检查返回表达式是否为对当前函数的调用
        if (_function != null && expression is BoundCallExpression callExpr && callExpr.Function == _function)
        {
            returnStatement.IsTailCall = true;
            returnStatement.TailCallFunction = _function;
        }

        return returnStatement;
    }

    private BoundStmt BindConstantDeclaration(ConstantDeclStmt syntax)
    {
        var boundexpr = BindExpression(syntax.Expression);
        if (boundexpr.ConstantValue == Value.Void)
        {
            _diagnostics.ReportInvalidConstantExpression(syntax.Location);
            return BindErrorStatement(syntax);
        }

        // Check if constant already exists
        var existingVar = _scope.TryLookupVar(syntax.Constant.Tag);
        if (existingVar != null)
        {
            _diagnostics.ReportConstantAlreadyDefined(syntax.Constant.Syntax);
            return BindErrorStatement(syntax);
        }

        var variable = LookupVariable(syntax.Constant, true, boundexpr.Type);

        // 将常量值写入 symbol，后续引用通过 BoundVariableExpression.ConstantValue 自动内联
        variable.Value = boundexpr.ConstantValue;

        // 声明本身消除，运行时不再执行赋值
        return new BoundNop(syntax);
    }

    private BoundStmt BindAssignStatement(AssignmentStmt syntax)
    {
        var target = syntax.Target;

        // Discard: _ = expr → evaluate for side effects, don't store
        if (target is DiscardExpr)
        {
            var discardExpr = BindExpression(syntax.Expression);
            return new BoundExprStatement(syntax, discardExpr);
        }

        // Variable: $var = expr
        if (target is VariableExpr varTarget)
        {
            return BindVariableAssign(syntax, varTarget);
        }

        // Field access: $var.field = expr
        if (target is FieldAccessExpr fieldTarget)
        {
            return BindFieldAssign(syntax, fieldTarget);
        }

        // Index access: $var[i] = expr
        if (target is IndexVisitExpression indexTarget)
        {
            return BindIndexAssign(syntax, indexTarget);
        }

        _diagnostics.ReportUnexpectedToken(syntax.Location, syntax.Target.Syntax, TokenType.VAR);
        return BindErrorStatement(syntax);
    }

    private BoundStmt BindVariableAssign(AssignmentStmt syntax, VariableExpr varTarget)
    {
        var boundexpr = BindExpression(syntax.Expression);

        var desugared = DesugarAugmentedAssign(syntax, () => BindVarExpression(varTarget), boundexpr.Type, boundexpr);
        if (desugared is not null) boundexpr = desugared;

        if (boundexpr.Type == ScriptType.Void)
        {
            _diagnostics.ReportVoidExpressionCannotAssign(syntax.Location);
            return BindErrorStatement(syntax);
        }
        var variable = BindVariableDeclaration(varTarget, varTarget.ReadOnly, boundexpr.Type);

        if (!variable.Type.IsAssignableFrom(boundexpr.Type))
            _diagnostics.ReportCannotConvert(syntax.Location, boundexpr.Type, variable.Type);

        return new BoundVariableDeclaration(syntax, variable, boundexpr);
    }

    private BoundStmt BindFieldAssign(AssignmentStmt syntax, FieldAccessExpr fieldTarget)
    {
        var boundTarget = BindExpression(fieldTarget.Target);

        var resolved = TryResolveStructField(boundTarget, fieldTarget.FieldName, syntax.Location);
        if (resolved is null) return BindErrorStatement(syntax);
        var (structType, field) = resolved.Value;

        var fieldType = FieldTypeToScriptType(field);
        var boundValue = BindExpression(syntax.Expression);

        var desugared = DesugarAugmentedAssign(syntax, () => new BoundFieldAccessExpression(syntax, boundTarget, field, fieldType), fieldType, boundValue);
        if (desugared is not null) boundValue = desugared;

        boundValue = BindConversion(boundValue, fieldType);
        return new BoundFieldAssignStatement(syntax, boundTarget, field, boundValue);
    }

    private BoundStmt BindIndexAssign(AssignmentStmt syntax, IndexVisitExpression indexTarget)
    {
        var boundContainer = BindExpression(indexTarget.Base);
        var boundIndex = BindConversion(BindExpression(indexTarget.Index), ScriptType.Int);

        var (isString, isArray) = CheckIndexSupport(boundContainer.Type);
        if (!isString && !isArray)
        {
            _diagnostics.ReportTypeDoesNotSupportIndexAccess(syntax.Location, boundContainer.Type);
            return BindErrorStatement(syntax);
        }

        if (isString)
        {
            _diagnostics.ReportBadStruct(syntax.Location, "字符串不支持元素赋值");
            return BindErrorStatement(syntax);
        }

        var elemType = ((GenericType)boundContainer.Type).TypeArguments[0];
        var boundValue = BindExpression(syntax.Expression);

        var desugared = DesugarAugmentedAssign(syntax, () => new BoundIndexVariableExpression(syntax, boundContainer, boundIndex, elemType), elemType, boundValue);
        if (desugared is not null) boundValue = desugared;

        boundValue = BindConversion(boundValue, elemType);
        return new BoundIndexAssignStatement(syntax, boundContainer, boundIndex, boundValue);
    }

    private BoundExpr BindExpression(ExprBase syntax)
    {
        return syntax switch
        {
            LiteralExpr => BindLiterExpression((LiteralExpr)syntax),
            ExtVarExpr => BindExtraLabel((ExtVarExpr)syntax),
            UnaryExpression => BindUnaryExpression((UnaryExpression)syntax),
            BinaryExpression => BindBinaryExpression((BinaryExpression)syntax),
            ParenthesizedExpression pre => BindExpression(pre.Expression),
            Callv1Expression => BindCallExpression((Callv1Expression)syntax),
            IndexDefExpression => BindIndexExpression((IndexDefExpression)syntax),
            IndexVisitExpression => BindIndexVisitExpression((IndexVisitExpression)syntax),
            SliceExpression => BindSliceExpression((SliceExpression)syntax),
            VariableExpr => BindVarExpression((VariableExpr)syntax),
            StructInitExpr => BindStructInitExpression((StructInitExpr)syntax),
            FieldAccessExpr => BindFieldAccessExpression((FieldAccessExpr)syntax),
            _ => ReportUnknownExprAndError(syntax),
        };
    }

    private BoundExpr ReportUnknownExprAndError(ExprBase syntax)
    {
        _diagnostics.ReportUnknownExpressionType(syntax.Syntax.Location);
        return new BoundErrorExpression(syntax);
    }

    private BoundExpr BindExtraLabel(ExtVarExpr syntax)
    {
        var name = syntax.Name;

        // 在 binder 阶段验证外部变量名称
        if (!_scope.TryFindoutLabel(name))
        {
            _diagnostics.ReportImageLabelNotFound(syntax.Syntax.Location, name);
            return new BoundErrorExpression(syntax);
        }

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
                if (input[0] == '"' || input[0] == '\'')
                    val = input[1..^1];
            }
        }
        Value obj = Value.From(val);
        return new BoundLiteralExpression(syntax, obj);
    }

    private BoundExpr BindIndexExpression(IndexDefExpression syntax)
    {
        if (syntax.Index.Length == 0)
            return new BoundIndexDeclxpression(syntax, []);
        var boundIndexs = ImmutableArray.CreateBuilder<BoundExpr>();

        foreach (var index in syntax.Index)
        {
            var boundIndex = BindExpression(index);
            boundIndexs.Add(boundIndex);
        }
        if (boundIndexs.Select(i => i.Type).Distinct().Count() > 1)
        {
            _diagnostics.ReportArrayElementTypeMismatch(syntax.Syntax.Location);
            return new BoundErrorExpression(syntax);
        }

        return new BoundIndexDeclxpression(syntax, boundIndexs.ToImmutable());
    }

    private BoundExpr BindIndexVisitExpression(IndexVisitExpression syntax)
    {
        var baseExpr = BindExpression(syntax.Base);

        var (isString, isArray) = CheckIndexSupport(baseExpr.Type);
        if (!isString && !isArray)
        {
            _diagnostics.ReportTypeDoesNotSupportIndexAccess(syntax.Syntax.Location, baseExpr.Type);
            return new BoundErrorExpression(syntax);
        }

        var indexExpr = BindConversion(syntax.Index, ScriptType.Int);

        ScriptType resultType = isString
            ? ScriptType.String
            : ((GenericType)baseExpr.Type).TypeArguments[0];

        return new BoundIndexVariableExpression(syntax, baseExpr, indexExpr, resultType);
    }

    private BoundExpr BindSliceExpression(SliceExpression syntax)
    {
        var baseExpr = BindExpression(syntax.Base);

        var (isString, isArray) = CheckIndexSupport(baseExpr.Type);
        if (!isString && !isArray)
        {
            _diagnostics.ReportTypeDoesNotSupportSlice(syntax.Syntax.Location, baseExpr.Type);
            return new BoundErrorExpression(syntax);
        }

        var startExpr = BindConversion(syntax.Start, ScriptType.Int);
        var endExpr = BindConversion(syntax.End, ScriptType.Int);

        return new BoundSliceExpression(syntax, baseExpr, startExpr, endExpr, baseExpr.Type);
    }

    private BoundExpr BindConversion(BoundExpr expr, ScriptType type)
    {
        if (type.IsAssignableFrom(expr.Type)) return expr;
        if (type == ScriptType.String) return new BoundConversionExpression(expr.Syntax, type, expr);
        if (type == ScriptType.Double && expr.Type.Equals(ScriptType.Int)) return new BoundConversionExpression(expr.Syntax, type, expr);
        _diagnostics.ReportCannotConvert(expr.Syntax.Syntax.Location, expr.Type, type);
        return new BoundErrorExpression(expr.Syntax);
    }

    private BoundExpr BindConversion(ExprBase syntax, ScriptType type)
    {
        var expr = BindExpression(syntax);
        if (expr.Type != type)
            _diagnostics.ReportCannotConvert(expr.Syntax.Syntax.Location, expr.Type, type);
        return expr;
    }

    private BoundExpr BindVarExpression(VariableExpr syntax)
    {
        var variable = _scope.TryLookupVar(syntax.Tag);
        if (variable == null)
        {
            _diagnostics.ReportVariableNotFound(syntax.Syntax.Location, syntax.Tag);
            return new BoundErrorExpression(syntax);
        }
        return new BoundVariableExpression(syntax, variable);
    }

    private BoundExpr BindStructInitExpression(StructInitExpr syntax)
    {
        if (!_structDefs.TryGetValue(syntax.TypeName, out var def))
        {
            _diagnostics.ReportBadStruct(syntax.Syntax.Location, $"未知结构体类型 {syntax.TypeName}");
            return new BoundErrorExpression(syntax);
        }
        return new BoundStructInitExpression(syntax, def);
    }

    private BoundExpr BindFieldAccessExpression(FieldAccessExpr syntax)
    {
        var boundTarget = BindExpression(syntax.Target);

        var resolved = TryResolveStructField(boundTarget, syntax.FieldName, syntax.Syntax.Location);
        if (resolved is null) return new BoundErrorExpression(syntax);
        var (_, field) = resolved.Value;

        var resultType = FieldTypeToScriptType(field);
        return new BoundFieldAccessExpression(syntax, boundTarget, field, resultType);
    }

    private BoundExpr BindUnaryExpression(UnaryExpression syntax)
    {
        var boundOperand = BindExpression(syntax.Operand);

        var boundOperator = BoundUnaryOperator.Bind(syntax.Operator.Type, boundOperand.Type);
        if (boundOperator == null)
        {
            _diagnostics.ReportUnsupportedUnaryOperator(syntax.Syntax.Location, syntax.Operator, boundOperand.Type);
            return new BoundErrorExpression(syntax);
        }

        return new BoundUnaryExpression(syntax, boundOperator, boundOperand);
    }

    private BoundExpr BindBinaryExpression(BinaryExpression syntax)
    {
        var boundLeft = BindExpression(syntax.ValueLeft);
        var boundRight = BindExpression(syntax.ValueRight);

        var boundOperator = BoundBinaryOperator.Bind(syntax.Operator.Type, boundLeft.Type, boundRight.Type);
        if (boundOperator == null)
        {
            _diagnostics.ReportUnsupportedBinaryOperator(syntax.Syntax.Location, syntax.Operator, boundLeft.Type, boundRight.Type);
            return new BoundErrorExpression(syntax);
        }

        if (boundLeft.ConstantValue != Value.Void && boundRight.ConstantValue != Value.Void)
        {
            var COS = boundOperator.Operate(boundLeft.ConstantValue, boundRight.ConstantValue);
            return new BoundLiteralExpression(syntax, COS);
        }

        return new BoundBinaryExpression(syntax, boundLeft, boundOperator, boundRight);
    }

    private BoundExpr BindCallExpressionInternal(AstNode syntax, FunctionSymbol function, ImmutableArray<ExprBase> Arguments)
    {
        // 1. 先绑定实参表达式
        var boundArgs = Arguments.Select(BindExpression).ToImmutableArray();

        // 2. 检查参数数量
        int minArgs = function.Parameters.Count(p => !p.HasDefaultValue);
        int maxArgs = function.Parameters.Length;

        if (boundArgs.Length < minArgs || boundArgs.Length > maxArgs)
        {
            _diagnostics.ReportFunctionArgumentCountMismatch(syntax.Syntax.Location, function);
            return new BoundErrorExpression(syntax);
        }

        // 3. 用已绑定的实参做泛型推导和类型转换
        return BuildCallWithTypeConversion(syntax, function, boundArgs);
    }

    /// <summary>
    /// 从已绑定的实参出发，做泛型推导和类型转换，生成最终的 BoundCallExpression。
    /// </summary>
    private BoundExpr BuildCallWithTypeConversion(AstNode syntax, FunctionSymbol function, ImmutableArray<BoundExpr> boundArgs)
    {
        // 泛型绑定与实例化
        var (instParams, instReturn) = BindGenericFunction(syntax, function, boundArgs);

        // 类型转换与最终参数确定
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

    /// <summary>
    /// 重载解析：从同名候选函数中选出与实参类型最匹配的一个。
    /// </summary>
    private FunctionSymbol? ResolveOverload(AstNode syntax, string name, ImmutableArray<FunctionSymbol> candidates, ImmutableArray<BoundExpr> boundArgs)
    {
        // 单候选：仍需检查参数数量
        if (candidates.Length == 1)
        {
            var c = candidates[0];
            if (!MatchesArgCount(c, boundArgs.Length))
            {
                _diagnostics.ReportFunctionArgumentCountMismatch(syntax.Syntax.Location, c);
                return null;
            }
            return c;
        }

        FunctionSymbol? best = null;
        int bestScore = -1;
        bool ambiguous = false;

        foreach (var candidate in candidates)
        {
            int minArgs = candidate.Parameters.Count(p => !p.HasDefaultValue);
            int maxArgs = candidate.Parameters.Length;

            // 参数数量筛选
            if (boundArgs.Length < minArgs || boundArgs.Length > maxArgs)
                continue;

            // 逐参数打分
            int score = 0;
            bool eliminated = false;
            for (int i = 0; i < boundArgs.Length && i < candidate.Parameters.Length; i++)
            {
                var paramType = candidate.Parameters[i].Type;
                var argType = boundArgs[i].Type;

                if (paramType is TypeParameter || ContainsTypeParameter(paramType))
                {
                    // 泛型参数可匹配任何类型
                    score += 1;
                }
                else if (paramType.IsAssignableFrom(argType))
                {
                    // 精确类型匹配
                    score += 2;
                }
                else if (argType != ScriptType.String && paramType == ScriptType.String)
                {
                    // 隐式转换到 string
                    score += 0;
                }
                else
                {
                    eliminated = true;
                    break;
                }
            }

            if (eliminated) continue;

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
                ambiguous = false;
            }
            else if (score == bestScore)
            {
                ambiguous = true;
            }
        }

        if (best == null)
        {
            _diagnostics.ReportNoMatchingOverload(syntax.Syntax.Location, name,
                [.. boundArgs.Select(a => a.Type)]);
            return null;
        }

        if (ambiguous)
        {
            _diagnostics.ReportAmbiguousCall(syntax.Syntax.Location, name,
                [.. candidates.Where(c => MatchesArgCount(c, boundArgs.Length))]);
            return null;
        }

        return best;
    }

    private static bool ContainsTypeParameter(ScriptType type)
    {
        if (type is TypeParameter) return true;
        if (type is GenericType gt)
            return gt.TypeArguments.Any(ContainsTypeParameter);
        return false;
    }

    private static bool MatchesArgCount(FunctionSymbol fn, int argCount)
    {
        int minArgs = fn.Parameters.Count(p => !p.HasDefaultValue);
        int maxArgs = fn.Parameters.Length;
        return argCount >= minArgs && argCount <= maxArgs;
    }

    private BoundExpr BindCallExpression(Callv1Expression syntax)
    {
        var candidates = _scope.TryLookupFuncs(syntax.Identifier.Value);
        if (candidates.IsEmpty)
        {
            _diagnostics.ReportFunctionNotFound(syntax.Identifier.Location, syntax.Identifier.Value);
            return new BoundErrorExpression(syntax);
        }

        if (candidates.Length == 1)
        {
            EnsureFunctionBodyBound(candidates[0]);
            return BindCallExpressionInternal(syntax, candidates[0], syntax.Arguments);
        }

        // 多候选：先绑实参，再做重载解析
        var boundArgs = syntax.Arguments.Select(BindExpression).ToImmutableArray();
        var function = ResolveOverload(syntax, syntax.Identifier.Value, candidates, boundArgs);
        if (function == null)
            return new BoundErrorExpression(syntax);

        EnsureFunctionBodyBound(function);

        return BuildCallWithTypeConversion(syntax, function, boundArgs);
    }

    private BoundStmt BindCallStatement(CallStmt syntax)
    {
        var name = syntax.FnName;
        if (BuiltinFunctions.GetAll().Select(f => f.Name).Contains(syntax.FnName.ToUpper()))
        {
            name = syntax.FnName.ToUpper();
        }

        var candidates = _scope.TryLookupFuncs(name);
        if (candidates.IsEmpty)
        {
            _diagnostics.ReportFunctionNotFound(syntax.Location, name);
            return BindErrorStatement(syntax);
        }

        // 传统兼容模式（仅对 builtin 单候选时走旧路径）
        if (SyntaxTree.LegacyCompat && syntax.Args.Length == 1 && syntax.Args[0] is VariableExpr legacyVar)
        {
            var builtinFunc = candidates.FirstOrDefault(c => BuiltinFunctions.GetAll().Contains(c));
            if (builtinFunc != null)
            {
                if (BuiltinFunctions.Timestamp == builtinFunc)
                {
                    var timeCallExpr = BindCallExpressionInternal(syntax, builtinFunc, []);
                    var variable = BindVariableDeclaration(legacyVar, false, ScriptType.Int);
                    return new BoundVariableDeclaration(syntax, variable, timeCallExpr);
                }
                if (BuiltinFunctions.Rand == builtinFunc)
                {
                    var randCallExpr = BindCallExpressionInternal(syntax, builtinFunc, [legacyVar]);
                    var variable = BindVariableDeclaration(legacyVar, false, ScriptType.Int);
                    return new BoundVariableDeclaration(syntax, variable, randCallExpr);
                }
            }
        }

        // 绑定实参表达式（重载解析需要知道实参类型）
        var boundArgs = syntax.Args.Select(BindExpression).ToImmutableArray();
        var function = ResolveOverload(syntax, name, candidates, boundArgs);
        if (function == null)
            return BindErrorStatement(syntax);

        EnsureFunctionBodyBound(function);

        // 用已确定的函数做类型转换
        var expr = BuildCallWithTypeConversion(syntax, function, boundArgs);
        return new BoundExprStatement(syntax, expr);
    }

    private BoundExprStatement BindWaitStatement(Wait syntax)
    {
        var boundArgument = BindExpression(syntax.Duration);
        boundArgument = BindConversion(boundArgument, ScriptType.Int);

        var expr = new BoundCallExpression(syntax, BuiltinFunctions.Wait, [boundArgument], ScriptType.Void);
        return new BoundExprStatement(syntax, expr);
    }

    private BoundStmt BindGamepadActionStatement(KeyActionStmt syntax)
    {
        if (syntax is StickActionStmt st)
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
}
