using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using System.Collections.Immutable;

namespace EasyCon.Script.Binding;

internal sealed partial class Binder
{
    #region 表达式绑定

    private BoundExpr BindExpression(BaseExpr syntax)
    {
        return syntax switch
        {
            LiteralExpr lit => BindLiterExpression(lit),
            RuntimeValueExpr rv => BindRuntimeValue(rv),
            ImageLabelExpr il => BindImageLabel(il),
            UnaryExpression unary => BindUnaryExpression(unary),
            BinaryExpression binary => BindBinaryExpression(binary),
            ParenthesizedExpression pre => BindExpression(pre.Expression),
            Callv1Expression call => BindCallExpression(call),
            IndexDefExpression idxDef => BindIndexExpression(idxDef),
            IndexVisitExpression idxVisit => BindIndexVisitExpression(idxVisit),
            SliceExpression slice => BindSliceExpression(slice),
            VariableExpr var => BindVarExpression(var),
            ConstVarExpr c => BindVarExpression_ByTag(c.Syntax, c.Tag),
            StructInitExpr init => BindStructInitExpression(init),
            FieldAccessExpr field => BindFieldAccessExpression(field),
            _ => ReportUnknownExprAndError(syntax),
        };
    }

    private BoundExpr BindRuntimeValue(RuntimeValueExpr syntax)
    {
        if (Formatter.IsSpecialConst(syntax.Name))
            return new BoundRuntimeValueExpression(syntax, syntax.Name, Formatter.SpecialConsts[syntax.Name]);

        return new BoundErrorExpression(syntax);
    }

    private BoundExpr BindImageLabel(ImageLabelExpr syntax)
    {
        if (!_scope.TryFindoutLabel(syntax.Name))
        {
            _diagnostics.ReportImageLabelNotFound(syntax.Syntax.Location, syntax.Name);
            return new BoundErrorExpression(syntax);
        }

        _ilNames.Add(syntax.Name);
        return new BoundImageLabelExpression(syntax, syntax.Name);
    }

    private BoundLiteralExpression BindLiterExpression(LiteralExpr syntax)
    {
        var val = syntax.Value;
        if (syntax.Value is string input)
        {
            if (input.Length >= 2 && input[0] == input[^1])
            {
                if (input[0] == '"' || input[0] == '\'')
                    val = ProcessEscapeSequences(input.AsSpan()[1..^1]);
            }
        }
        Value obj = Value.From(val);
        return new BoundLiteralExpression(syntax, val, obj.Type);
    }

    private static string ProcessEscapeSequences(ReadOnlySpan<char> s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                sb.Append(s[++i] switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    '\\' => '\\',
                    '\'' => '\'',
                    '"' => '"',
                    var c => c,
                });
            }
            else
            {
                sb.Append(s[i]);
            }
        }
        return sb.ToString();
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

        if (baseExpr is BoundFieldAccessExpression fieldAccess && fieldAccess.Field.FieldType is ArrayType arrType)
        {
            var indexExpr = BindConversion(BindExpression(syntax.Index), ScriptType.Int);
            return new BoundFieldIndexAccessExpression(syntax, fieldAccess.Target, fieldAccess.Field, indexExpr, arrType.ElementType);
        }

        var (isString, isArray) = CheckIndexSupport(baseExpr.Type);
        if (!isString && !isArray)
        {
            _diagnostics.ReportTypeDoesNotSupportIndexAccess(syntax.Syntax.Location, baseExpr.Type);
            return new BoundErrorExpression(syntax);
        }

        var indexExpr2 = BindConversion(syntax.Index, ScriptType.Int);

        // 编译期数组越界检查
        if (baseExpr is BoundVariableExpression bve
            && _arrayLengths.TryGetValue(bve.Variable.Name, out var knownLen))
            CheckArrayBounds(syntax.Syntax.Location, indexExpr2, knownLen);

        ScriptType resultType2 = isString
            ? ScriptType.String
            : ((ArrayType)baseExpr.Type).ElementType;

        return new BoundIndexVariableExpression(syntax, baseExpr, indexExpr2, resultType2);
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
        var endExpr = syntax.End is LiteralExpr { Value: "" }
            ? BindExpression(syntax.End)
            : BindConversion(syntax.End, ScriptType.Int);

        return new BoundSliceExpression(syntax, baseExpr, startExpr, endExpr, baseExpr.Type);
    }

    #endregion

    #region 类型转换

    private BoundExpr BindConversion(BoundExpr expr, ScriptType type)
    {
        if (type.IsAssignableFrom(expr.Type)) return expr;
        if (type == ScriptType.String
            || type == ScriptType.Double
            || (type == ScriptType.Ptr && expr.Type is StructType)
            || (type is StructType && expr.Type.Equals(ScriptType.Ptr))
            || (type.Equals(ScriptType.UInt) && expr.Type.Equals(ScriptType.Int))
            || (type.Equals(ScriptType.UInt64) && expr.Type.Equals(ScriptType.Int))
            || (type.Equals(ScriptType.UInt64) && expr.Type.Equals(ScriptType.UInt))
            || (type.Equals(ScriptType.Byte) && expr.Type.Equals(ScriptType.Int)))
            return new BoundConversionExpression(expr.Syntax, type, expr);
        _diagnostics.ReportCannotConvert(expr.Syntax.Syntax.Location, expr.Type, type);
        return new BoundErrorExpression(expr.Syntax);
    }

    private BoundExpr BindConversion(BaseExpr syntax, ScriptType type)
    {
        var expr = BindExpression(syntax);
        if (expr.Type != type)
            _diagnostics.ReportCannotConvert(expr.Syntax.Syntax.Location, expr.Type, type);
        return expr;
    }

    #endregion

    #region 变量与结构体表达式

    private BoundExpr BindVarExpression(VariableExpr syntax)
    {
        return BindVarExpression_ByTag(syntax.Syntax, syntax.Tag, syntax);
    }

    private BoundExpr BindVarExpression_ByTag(Token syntaxToken, string tag, BaseExpr? originalExpr = null)
    {
        var variable = _scope.TryLookupVar(tag);
        if (variable == null)
        {
            _diagnostics.ReportVariableNotFound(syntaxToken.Location, tag);
            return new BoundErrorExpression(originalExpr ?? new VariableExpr(syntaxToken));
        }
        return new BoundVariableExpression(originalExpr ?? new VariableExpr(syntaxToken), variable);
    }

    private BoundExpr BindStructInitExpression(StructInitExpr syntax)
    {
        if (_scope.TryLookupStruct(syntax.TypeName) is not { } def)
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

        var resultType = field.FieldType is ArrayType arrType
            ? ScriptType.ArrayOf(arrType.ElementType)
            : field.FieldType;
        return new BoundFieldAccessExpression(syntax, boundTarget, field, resultType);
    }

    #endregion

    #region 一元与二元表达式

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

        if (boundLeft.ConstantValue != null && boundRight.ConstantValue != null)
        {
            var l = Value.From(boundLeft.ConstantValue);
            var r = Value.From(boundRight.ConstantValue);
            l = FoldConvert(l, boundOperator.LeftType);
            r = FoldConvert(r, boundOperator.RightType);
            var result = boundOperator.Kind switch
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
                _ => throw new InvalidOperationException($"不支持的常量运算: {boundOperator.Kind}")
            };
            return new BoundLiteralExpression(syntax, result.ToObject(), result.Type);
        }

        return new BoundBinaryExpression(syntax, boundLeft, boundOperator, boundRight);
    }

    private static Value FoldConvert(Value v, ScriptType targetType)
    {
        if (v.Type.Equals(targetType)) return v;
        if (targetType.Equals(ScriptType.Double)) return Value.FromDouble(v.AsInt());
        if (targetType.Equals(ScriptType.UInt)) return Value.FromUInt(unchecked((uint)v.AsInt()));
        if (targetType.Equals(ScriptType.UInt64)) return Value.FromUInt64((ulong)v.AsInt());
        if (targetType.Equals(ScriptType.Byte)) return Value.FromByte((byte)v.AsInt());
        if (targetType.Equals(ScriptType.Ptr)) return Value.FromPtr((long)v.AsInt());
        return v;
    }

    #endregion
}