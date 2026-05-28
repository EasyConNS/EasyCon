using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using System.Collections.Immutable;
using static EasyCon.Script.Binding.BoundFactory;

namespace EasyCon.Script.Binding;

internal sealed partial class Binder
{
    #region 语句绑定

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
                UntilBlock => BindUntil((UntilBlock)syntax),
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

    #endregion

    #region 控制流

    private BoundIfStatement BindIf(IfBlock syntax)
    {
        var condition = BindConversion(syntax.Condition.Condition, ScriptType.Bool);
        static bool isCtrl(Statement st) => st is ElseIf or Else or EndIf;

        var index = 0;
        _scope = new BoundScope(_scope);
        var bodyStmts = ImmutableArray.CreateBuilder<BoundStmt>();
        while (index < syntax.Statements.Length && !isCtrl(syntax.Statements[index]))
        {
            bodyStmts.Add(BindStatement(syntax.Statements[index]));
            index++;
        }
        _scope = _scope.Parent!;
        var body = new BoundBlockStatement(syntax, bodyStmts.ToImmutable());

        var elseIfs = ImmutableArray.CreateBuilder<(BoundExpr Condition, BoundBlockStatement Body)>();
        while (index < syntax.Statements.Length && syntax.Statements[index] is ElseIf elifCond)
        {
            var elifCondition = BindConversion(elifCond.Condition, ScriptType.Bool);
            index++;
            _scope = new BoundScope(_scope);
            var elifStmts = ImmutableArray.CreateBuilder<BoundStmt>();
            while (index < syntax.Statements.Length && !isCtrl(syntax.Statements[index]))
            {
                elifStmts.Add(BindStatement(syntax.Statements[index]));
                index++;
            }
            _scope = _scope.Parent!;
            elseIfs.Add((elifCondition, new BoundBlockStatement(syntax, elifStmts.ToImmutable())));
        }

        BoundBlockStatement? elseBody = null;
        if (index < syntax.Statements.Length && syntax.Statements[index] is Else)
        {
            index++;
            _scope = new BoundScope(_scope);
            var elseStmts = ImmutableArray.CreateBuilder<BoundStmt>();
            while (index < syntax.Statements.Length && !isCtrl(syntax.Statements[index]))
            {
                elseStmts.Add(BindStatement(syntax.Statements[index]));
                index++;
            }
            _scope = _scope.Parent!;
            elseBody = new BoundBlockStatement(syntax, elseStmts.ToImmutable());
        }

        return new BoundIfStatement(syntax, condition, body, elseIfs.ToImmutable(), elseBody);
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

        BoundStmt lowerBoundStmt = variable == null ? Nop(forCond) :
            VariableDeclaration(forCond, variable, lowerBound);
        BoundStmt upperBoundStmt = variable == null ? Nop(forCond) :
            ConstantDeclaration(forCond, "_uppBound$", upperBound);
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
        var step = Literal(forCond.Upper, 1);
        BoundStmt stepStmt = variable == null ? Nop(forCond) :
            VariableDeclaration(syntax, variable, Add(forCond.Upper, Variable(forCond.Lower, variable), step));

        var body = BindLoopBody(syntax, syntax.Statements, out var breakLabel, out var continueLabel);
        _scope = _scope.Parent!;

        BoundStmt breakIfEnd = variable == null ? Nop(syntax) :
            GotoTrue(syntax, breakLabel, new BoundBinaryExpression(syntax,
                    Variable(forCond.Lower, variable),
                    BoundBinaryOperator.Bind(TokenType.EQL, ScriptType.Int, ScriptType.Int)!,
                    upperBound));
        var lowwhile = new BoundWhileStatement(syntax,
             condition,
             Block(syntax,
                body,
                breakIfEnd,
                Label(syntax, continueLabel),
                stepStmt
                ),
             breakLabel,
             new BoundLabel($"label{++_labelCounter}")
             );

        return Block(syntax,
            lowerBoundStmt,
            upperBoundStmt,
            lowwhile
             );
    }

    private BoundWhileStatement BindWhile(WhileBlock syntax)
    {
        var body = BindLoopBody(syntax, syntax.Statements, out var breakLabel, out var continueLabel);

        return new BoundWhileStatement(syntax,
            BindConversion(syntax.Condition.Condition, ScriptType.Bool),
            body,
            breakLabel,
            continueLabel
             );
    }

    private BoundWhileStatement BindUntil(UntilBlock syntax)
    {
        var body = BindLoopBody(syntax, syntax.Statements, out var breakLabel, out var continueLabel);

        var boundCondition = BindConversion(syntax.Condition.Condition, ScriptType.Bool);
        var skipBreakLabel = new BoundLabel($"skipBreak{++_labelCounter}");
        var newBody = Block(syntax,
            GotoFalse(syntax.Condition, skipBreakLabel, boundCondition),
            Goto(syntax.Condition, breakLabel),
            Label(syntax.Condition, skipBreakLabel),
            body
        );

        return new BoundWhileStatement(syntax,
            new BoundLiteralExpression(syntax, true, ScriptType.Bool),
            newBody,
            breakLabel,
            continueLabel
        );
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
                    expression = BindConversion(expression, _function.ReturnType);
                }
            }
        }

        var returnStatement = new BoundReturnStatement(syntax, expression);

        if (_function != null && expression is BoundCallExpression callExpr && callExpr.Function == _function)
        {
            returnStatement.IsTailCall = true;
            returnStatement.TailCallFunction = _function;
        }

        return returnStatement;
    }

    #endregion

    #region 常量与赋值

    private BoundStmt BindConstantDeclaration(ConstantDeclStmt syntax)
    {
        if (Formatter.IsSpecialConst(syntax.Constant.Tag))
        {
            _diagnostics.ReportCannotAssignToSpecialConstant(syntax.Constant.Syntax);
            return BindErrorStatement(syntax);
        }

        var boundexpr = BindExpression(syntax.Expression);
        if (boundexpr.ConstantValue == null)
        {
            _diagnostics.ReportInvalidConstantExpression(syntax.Location);
            return BindErrorStatement(syntax);
        }

        var existingVar = _scope.TryLookupVar(syntax.Constant.Tag);
        if (existingVar != null)
        {
            _diagnostics.ReportConstantAlreadyDefined(syntax.Constant.Syntax);
            return BindErrorStatement(syntax);
        }

        var variable = LookupVariable(syntax.Constant, true, boundexpr.Type);

        variable.Value = boundexpr.ConstantValue;

        return new BoundNop(syntax);
    }

    private BoundStmt BindAssignStatement(AssignmentStmt syntax)
    {
        switch (syntax.Target)
        {
            case DiscardExpr:
                return new BoundExprStatement(syntax, BindExpression(syntax.Expression));
            case VariableExpr varTarget:
                return BindVariableAssign(syntax, varTarget);
            case FieldAccessExpr fieldTarget:
                return BindFieldAssign(syntax, fieldTarget);
            case IndexVisitExpression indexTarget:
                return BindIndexAssign(syntax, indexTarget);
            default:
                _diagnostics.ReportUnexpectedToken(syntax.Location, syntax.Target.Syntax, TokenType.VAR);
                return BindErrorStatement(syntax);
        }
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

        if (variable.Type is StructType && boundexpr.Type.Equals(ScriptType.Ptr))
            boundexpr = new BoundConversionExpression(syntax, variable.Type, boundexpr);
        else if (!variable.Type.IsAssignableFrom(boundexpr.Type))
            _diagnostics.ReportCannotConvert(syntax.Location, boundexpr.Type, variable.Type);

        // 追踪已知长度的数组变量（从数组字面量初始化时）
        if (boundexpr is BoundIndexDeclxpression idxDecl && idxDecl.Items.Length > 0)
            _arrayLengths[varTarget.Tag] = idxDecl.Items.Length;

        // APPEND 调用使追踪长度 +1
        if (boundexpr is BoundCallExpression { Function.Name: "APPEND" }
            && _arrayLengths.TryGetValue(varTarget.Tag, out var prevLen))
        {
            _arrayLengths[varTarget.Tag] = prevLen + 1;
        }

        return new BoundVariableDeclaration(syntax, variable, boundexpr);
    }

    private BoundStmt BindFieldAssign(AssignmentStmt syntax, FieldAccessExpr fieldTarget)
    {
        var boundTarget = BindExpression(fieldTarget.Target);

        var resolved = TryResolveStructField(boundTarget, fieldTarget.FieldName, syntax.Location);
        if (resolved is null) return BindErrorStatement(syntax);
        var (structType, field) = resolved.Value;

        if (field.FieldType is ArrayType)
        {
            _diagnostics.ReportBadStruct(syntax.Location, $"数组字段 {field.Name} 不支持整体赋值，请使用 {field.Name}[index] 逐元素赋值");
            return BindErrorStatement(syntax);
        }

        var fieldType = field.FieldType;
        var boundValue = BindExpression(syntax.Expression);

        var desugared = DesugarAugmentedAssign(syntax, () => new BoundFieldAccessExpression(syntax, boundTarget, field, fieldType), fieldType, boundValue);
        if (desugared is not null) boundValue = desugared;

        boundValue = BindConversion(boundValue, fieldType);
        return new BoundFieldAssignStatement(syntax, boundTarget, field, boundValue);
    }

    private BoundStmt BindIndexAssign(AssignmentStmt syntax, IndexVisitExpression indexTarget)
    {
        var boundContainer = BindExpression(indexTarget.Base);

        if (boundContainer is BoundFieldAccessExpression fieldAccess && fieldAccess.Field.FieldType is ArrayType arrType)
        {
            var field = fieldAccess.Field;
            var boundIndex = BindConversion(BindExpression(indexTarget.Index), ScriptType.Int);
            var boundValue = BindExpression(syntax.Expression);

            var elemType = arrType.ElementType;
            var desugared = DesugarAugmentedAssign(syntax,
                () => new BoundFieldIndexAccessExpression(syntax, fieldAccess.Target, field, boundIndex, elemType),
                elemType, boundValue);
            if (desugared is not null) boundValue = desugared;

            boundValue = BindConversion(boundValue, elemType);
            return new BoundFieldIndexAssignStatement(syntax, fieldAccess.Target, field, boundIndex, boundValue);
        }

        var boundIndex2 = BindConversion(BindExpression(indexTarget.Index), ScriptType.Int);

        var (isString, isArray) = CheckIndexSupport(boundContainer.Type);
        if (!isString && !isArray)
        {
            _diagnostics.ReportTypeDoesNotSupportIndexAccess(syntax.Location, boundContainer.Type);
            return BindErrorStatement(syntax);
        }

        // 编译期数组越界检查
        if (boundContainer is BoundVariableExpression bve2
            && _arrayLengths.TryGetValue(bve2.Variable.Name, out var knownLen2))
            CheckArrayBounds(syntax.Location, boundIndex2, knownLen2);

        if (isString)
        {
            _diagnostics.ReportBadStruct(syntax.Location, "字符串不支持元素赋值");
            return BindErrorStatement(syntax);
        }

        var arrayElemType = ((ArrayType)boundContainer.Type).ElementType;
        var boundValue2 = BindExpression(syntax.Expression);

        var desugared2 = DesugarAugmentedAssign(syntax, () => new BoundIndexVariableExpression(syntax, boundContainer, boundIndex2, arrayElemType), arrayElemType, boundValue2);
        if (desugared2 is not null) boundValue2 = desugared2;

        boundValue2 = BindConversion(boundValue2, arrayElemType);
        return new BoundIndexAssignStatement(syntax, boundContainer, boundIndex2, boundValue2);
    }

    private BoundExpr? DesugarAugmentedAssign(AssignmentStmt syntax, Func<BoundExpr> readCurrent, ScriptType targetType, BoundExpr rhs)
    {
        if (!syntax.AssignmentToken.Type.OperatorIsAug())
            return null;

        var readExpr = readCurrent();
        var op = BoundBinaryOperator.Bind(syntax.AssignmentToken.Type, readExpr.Type, rhs.Type);
        if (op == null)
        {
            _diagnostics.ReportUnsupportedBinaryOperator(syntax.Location, syntax.AssignmentToken, targetType, rhs.Type);
            return null;
        }
        var convertedLeft = BindConversion(readExpr, op.LeftType);
        var convertedRight = BindConversion(rhs, op.RightType);
        return new BoundBinaryExpression(syntax.Expression, convertedLeft, op, convertedRight);
    }

    #endregion

    #region 特殊语句

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

    #endregion
}