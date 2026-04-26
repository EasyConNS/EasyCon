using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using EasyCon.Script.Text;
using System.Collections;

namespace EasyCon.Script;

internal sealed class DiagnosticBag : IEnumerable<Diagnostic>
{
    private readonly List<Diagnostic> _diagnostics = [];

    public IEnumerator<Diagnostic> GetEnumerator() => _diagnostics.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void AddRange(IEnumerable<Diagnostic> diagnostics)
    {
        _diagnostics.AddRange(diagnostics);
    }

    private void ReportWarnning(TextLocation location, string message)
    {
        var diagnostic = Diagnostic.Warning(location, message);
        _diagnostics.Add(diagnostic);
    }

    private void ReportError(TextLocation location, string message)
    {
        var diagnostic = Diagnostic.Error(location, message);
        _diagnostics.Add(diagnostic);
    }

    public void ReportInvalidNumber(TextLocation location, string text)
    {
        var message = $"数字格式不正确: {text}";
        ReportError(location, message);
    }

    public void ReportBadCharacter(TextLocation location, char character)
    {
        var message = $"不识别的字符: '{character}'";
        ReportError(location, message);
    }

    public void ReportUnterminatedString(TextLocation location)
    {
        var message = "字符串没有结束引号";
        ReportError(location, message);
    }

    public void ReportUnexpectedToken(TextLocation location, Token actual, TokenType expectedKind)
    {
        var message = $"需要<{expectedKind}>但是意外的<{actual.Value}>";
        ReportError(location, message);
    }

    public void ReportInvalidImport(TextLocation location, Token modToken)
    {
        var message = $"导入库不存在 ：{modToken.Value}";
        ReportError(location, message);
    }

    public void ReportInvalidExpressionStatement(TextLocation location)
    {
        var message = "无效的表达式语句";
        ReportError(location, message);
    }

    public void ReportInvalidKeyActionStatement(TextLocation location, Token actual)
    {
        var message = $"按键语法不正确 ：{actual}";
        ReportError(location, message);
    }
    public void ReportInvalidEOF(TextLocation location, Token actual)
    {
        var message = $"期望结束但多余的 ：{actual.Value}";
        ReportError(location, message);
    }

    public void ReportTooMuchLoop(TextLocation location)
    {
        var message = "循环层数过多，请优化脚本";
        ReportWarnning(location, message);
        ReportError(location, message);
    }

    public void ReportInvalidBreakOrContinue(TextLocation location, Token keyword)
    {
        var message = $"跳出语句 {keyword.Value} 循环层数不足";
        ReportError(location, message);
    }

    public void ReportBadStruct(TextLocation location, string message)
    {
        ReportError(location, message);
    }

    public void ReportOnlyOneFileCanHaveGlobalStatements(TextLocation location)
    {
        ReportError(location, "脚本主语句只能存在一个文件中");
    }

    public void ReportAllPathsMustReturn(TextLocation location)
    {
        ReportError(location, "函数所有路径必须有返回值");
    }

    public void ReportParameterAlreadyDeclared(TextLocation location, string paramName)
    {
        ReportError(location, $"重复定义的参数名: {paramName}");
    }

    public void ReportCannotConvert(TextLocation location, ScriptType fromType, ScriptType toType)
    {
        var message = $"类型不匹配：无法将 {fromType} 转换成 {toType}";
        ReportError(location, message);
    }

    public void ReportGenericTypeInferenceFailed(TextLocation location, string functionName, string typeName)
    {
        ReportError(location, $"无法为函数 {functionName} 推导泛型参数 {typeName}");
    }

    public void ReportGenericTypeConflict(TextLocation location, string typeName, ScriptType existingType, ScriptType actualType)
    {
        ReportError(location, $"泛型冲突：{typeName} 同时被推导为 {existingType} 和 {actualType}");
    }

    public void ReportVoidFunctionCannotReturn(TextLocation location, FunctionSymbol function, ScriptType type)
    {
        ReportError(location, $"函数 '{function.Name}' 返回类型为 void，不应有返回值");
    }

    public void ReportFunctionMustReturnValue(TextLocation location, FunctionSymbol function, ScriptType returnType)
    {
        ReportError(location, $"函数 '{function.Name}' 必须返回类型为 {returnType} 的值");
    }

    public void ReportInvalidConstantExpression(TextLocation location)
    {
        ReportError(location, "常量表达式不正确");
    }

    public void ReportConstantAlreadyDefined(Token constantToken)
    {
        ReportError(constantToken.Location, $"重复定义的常量 '{constantToken.Value}'");
    }

    public void ReportUnsupportedBinaryOperator(TextLocation location, Token opToken, ScriptType leftType, ScriptType rightType)
    {
        ReportError(location, $"不支持的运算符:{opToken.Value}对于类型 <{leftType}>和<{rightType}>");
    }

    public void ReportUnsupportedUnaryOperator(TextLocation location, Token opToken, ScriptType operandType)
    {
        ReportError(location, $"不支持的运算符:{opToken.Value}对于类型 <{operandType}>");
    }

    public void ReportVoidExpressionCannotAssign(TextLocation location)
    {
        ReportError(location, "空值表达式无法赋值");
    }

    public void ReportReadOnlyVariable(Token variableToken)
    {
        ReportError(variableToken.Location, $"只读变量无法修改：{variableToken.Value}");
    }

    public void ReportUnknownType(TextLocation location, Token typeToken)
    {
        ReportError(location, $"未知类型：{typeToken.Value}");
    }

    public void ReportFunctionNotFound(TextLocation location, string functionName)
    {
        ReportError(location, $"找不到调用函数 {functionName}");
    }

    public void ReportUnknownExpressionType(TextLocation location)
    {
        ReportError(location, "未知的表达式类型");
    }

    public void ReportImageLabelNotFound(TextLocation location, string labelName)
    {
        ReportError(location, $"找不到识图标签\"@{labelName}\"");
    }

    public void ReportArrayElementTypeMismatch(TextLocation location)
    {
        ReportError(location, "数组成员类型必须一致");
    }

    public void ReportTypeDoesNotSupportIndexAccess(TextLocation location, ScriptType type)
    {
        ReportError(location, $"类型 <{type}> 不支持索引访问");
    }

    public void ReportTypeDoesNotSupportSlice(TextLocation location, ScriptType type)
    {
        ReportError(location, $"类型 <{type}> 不支持切片操作");
    }

    public void ReportVariableNotFound(TextLocation location, string varName)
    {
        ReportError(location, $"找不到变量 {varName}");
    }

    public void ReportFunctionArgumentCountMismatch(TextLocation location, FunctionSymbol fn)
    {
        ReportError(location, $"函数 {fn.Name} 参数数量不匹配");
    }

    public void ReportFunctionAlreadyDeclared(TextLocation location, string functionName)
    {
        ReportError(location, $"重复定义的函数: {functionName}");
    }
}