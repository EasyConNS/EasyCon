using EasyCon.Script.Runtime;
using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using EasyCon.Script.Text;
using System.Collections.Immutable;
using System.Linq;

namespace EasyCon.Script.Binding;

internal sealed partial class Binder
{
    #region 共用辅助方法

    private (StructType structType, EcsFieldDef field)? TryResolveStructField(BoundExpr target, string fieldName, TextLocation loc)
    {
        if (target.Type is not StructType structType)
        {
            _diagnostics.ReportBadStruct(loc, $"类型 {target.Type} 不支持字段访问");
            return null;
        }

        var field = structType.Definition.Fields.FirstOrDefault(f => f.Name == fieldName);
        if (field == null)
        {
            _diagnostics.ReportBadStruct(loc, $"结构体 {structType.Name} 没有字段 {fieldName}");
            return null;
        }

        return (structType, field);
    }

    private static (bool isString, bool isArray) CheckIndexSupport(ScriptType type)
    {
        bool isString = type.Equals(ScriptType.String);
        bool isArray = type is ArrayType;
        return (isString, isArray);
    }

    private BoundExpr ReportUnknownExprAndError(BaseExpr syntax)
    {
        _diagnostics.ReportUnknownExpressionType(syntax.Syntax.Location);
        return new BoundErrorExpression(syntax);
    }

    private void CheckArrayBounds(TextLocation location, BoundExpr indexExpr, int arrayCount)
    {
        if (indexExpr.ConstantValue is int idx)
        {
            if (idx < 0)
                _diagnostics.ReportArrayIndexNegative(location, idx);
            else if (idx >= arrayCount)
                _diagnostics.ReportArrayIndexOutOfBounds(location, idx, arrayCount);
        }
    }

    #endregion
}