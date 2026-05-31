using EasyCon.Script.Runtime;
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

    private BoundStmt BindFor(ForBlock syntax)
    {
        var forCond = syntax.Condition;
        var kind = forCond switch
        {
            For_Infinite => ForKind.Infinite,
            For_Static => ForKind.Static,
            For_Full => ForKind.Full,
            _ => ForKind.Infinite
        };

        VariableSymbol? variable = null;
        BoundExpr? lowerBound = null;
        BoundExpr? upperBound = null;

        _scope = new BoundScope(_scope);

        if (kind == ForKind.Full || kind == ForKind.Static)
        {
            lowerBound = BindConversion(forCond.Lower, ScriptType.Int);
            upperBound = BindConversion(forCond.Upper, ScriptType.Int);

            var idxVar = forCond switch
            {
                For_Full ff => ff.RegIter,
                For_Static => new VariableExpr(new(syntax.Syntax.Text, TokenType.VAR, $"_tmpL${_labelCounter++}", 0), true),
                _ => null
            };
            if (idxVar != null)
                variable = BindVariableDeclaration(idxVar, isReadOnly: true, ScriptType.Int, allowGlobal: false);
        }

        var body = BindLoopBody(syntax, syntax.Statements, out var breakLabel, out var continueLabel);
        _scope = _scope.Parent!;

        return new BoundForStatement(syntax, kind, variable, lowerBound, upperBound, body, breakLabel, continueLabel);
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

    private BoundUntilStatement BindUntil(UntilBlock syntax)
    {
        var body = BindLoopBody(syntax, syntax.Statements, out var breakLabel, out var continueLabel);
        var boundCondition = BindConversion(syntax.Condition.Condition, ScriptType.Bool);
        return new BoundUntilStatement(syntax, boundCondition, body, breakLabel, continueLabel);
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

        return new BoundReturnStatement(syntax, expression);
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
        var constVal = TryEvaluateConstant(boundexpr);
        if (constVal == null)
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

        variable.Value = constVal;

        return new BoundNop(syntax);
    }

    /// <summary>
    /// 尝试编译期求值表达式（仅用于 CONST 声明验证）。
    /// 递归求值字面量、只读常量变量、二元运算，返回计算结果或 null。
    /// </summary>
    private static object? TryEvaluateConstant(BoundExpr expr)
    {
        switch (expr)
        {
            case BoundLiteralExpression lit:
                return lit.ConstantValue;

            case BoundVariableExpression var:
                return var.Variable is { IsReadOnly: true, Value: not null } ? var.Variable.Value : null;

            case BoundBinaryExpression bin:
                var left = TryEvaluateConstant(bin.Left);
                var right = TryEvaluateConstant(bin.Right);
                if (left == null || right == null) return null;
                return EvaluateBinaryConstant(left, bin.Op, right);

            default:
                return expr.ConstantValue;
        }
    }

    private static object? EvaluateBinaryConstant(object left, BoundBinaryOperator op, object right)
    {
        var l = Value.From(left);
        var r = Value.From(right);

        l = FoldConvertConstant(l, op.LeftType);
        r = FoldConvertConstant(r, op.RightType);

        var result = op.Kind switch
        {
            BoundBinaryOperatorKind.Addition => l + r,
            BoundBinaryOperatorKind.Subtraction => l - r,
            BoundBinaryOperatorKind.Multiplication => l * r,
            BoundBinaryOperatorKind.Division => l / r,
            BoundBinaryOperatorKind.Mod => l % r,
            BoundBinaryOperatorKind.RoundDiv => l.RoundDiv(r),
            BoundBinaryOperatorKind.BitwiseAnd => l & r,
            BoundBinaryOperatorKind.BitwiseOr => l | r,
            BoundBinaryOperatorKind.BitwiseXor => l ^ r,
            BoundBinaryOperatorKind.BitLeftShift => l << r,
            BoundBinaryOperatorKind.BitRightShift => l >> r,
            BoundBinaryOperatorKind.Equals => Value.FromBool(l.Equals(r)),
            BoundBinaryOperatorKind.NotEquals => Value.FromBool(!l.Equals(r)),
            BoundBinaryOperatorKind.Less => Value.FromBool(l < r),
            BoundBinaryOperatorKind.LessOrEquals => Value.FromBool(l <= r),
            BoundBinaryOperatorKind.Greater => Value.FromBool(l > r),
            BoundBinaryOperatorKind.GreaterOrEquals => Value.FromBool(l >= r),
            BoundBinaryOperatorKind.In => Value.FromBool(r.Contains(l)),
            BoundBinaryOperatorKind.LogicalAnd => Value.FromBool(l.AsBool() && r.AsBool()),
            BoundBinaryOperatorKind.LogicalOr => Value.FromBool(l.AsBool() || r.AsBool()),
            _ => (Value?)null
        };
        return result?.ToObject();
    }

    private static Value FoldConvertConstant(Value v, ScriptType targetType)
    {
        if (v.Type.Equals(targetType)) return v;
        if (targetType.Equals(ScriptType.Double)) return Value.FromDouble(v.AsInt());
        if (targetType.Equals(ScriptType.UInt)) return Value.FromUInt(unchecked((uint)v.AsInt()));
        if (targetType.Equals(ScriptType.UInt64)) return Value.FromUInt64((ulong)v.AsInt());
        if (targetType.Equals(ScriptType.Byte)) return Value.FromByte((byte)v.AsInt());
        if (targetType.Equals(ScriptType.Ptr)) return Value.FromPtr((long)v.AsInt());
        return v;
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

        var arrayElemType = ((ArrayType)boundContainer.Type).ElementType;
        var boundValue = BindExpression(syntax.Expression);

        var desugared = DesugarAugmentedAssign(syntax, () => new BoundIndexVariableExpression(syntax, boundContainer, boundIndex, arrayElemType), arrayElemType, boundValue);
        if (desugared is not null) boundValue = desugared;

        boundValue = BindConversion(boundValue, arrayElemType);
        return new BoundIndexAssignStatement(syntax, boundContainer, boundIndex, boundValue);
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