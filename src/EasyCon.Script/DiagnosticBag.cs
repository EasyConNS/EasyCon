using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using EasyCon.Script.Text;
using System.Collections;

namespace EasyCon.Script;

internal sealed class DiagnosticBag : IEnumerable<Diagnostic>
{
    private readonly List<Diagnostic> _diagnostics = [];

    /// <summary>
    /// 模块级诊断单点（.err 重放与异常包装共用的唯一构造形态）：
    /// 「[模块名] 前缀 + 默认落点」（落点经 <see cref="Modules.ModuleLocations.Default"/>，
    /// 无源码上下文时为零跨度 + 模块文件名）。两处消费方必须复用本方法，保证重放/包装文本形态一致。
    /// </summary>
    public static Diagnostic FromMessage(SyntaxTree? tree, string moduleName, string text)
        => Diagnostic.Error(Modules.ModuleLocations.Default(tree), $"[{moduleName}] {text}",
            DiagnosticCodes.ModuleCompileFailed);

    public IEnumerator<Diagnostic> GetEnumerator() => _diagnostics.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void AddRange(IEnumerable<Diagnostic> diagnostics)
    {
        _diagnostics.AddRange(diagnostics);
    }

    public void Add(Diagnostic diagnostic)
    {
        _diagnostics.Add(diagnostic);
    }

    private void ReportError(TextLocation location, string message, string code)
    {
        _diagnostics.Add(Diagnostic.Error(location, message, code));
    }

    public void ReportInvalidNumber(TextLocation location, string text)
    {
        ReportError(location, $"数字格式不正确: {text}", DiagnosticCodes.InvalidNumber);
    }

    public void ReportBadCharacter(TextLocation location, char character)
    {
        ReportError(location, $"不识别的字符: '{character}'", DiagnosticCodes.BadCharacter);
    }

    public void ReportUnterminatedString(TextLocation location)
    {
        ReportError(location, "字符串没有结束引号", DiagnosticCodes.UnterminatedString);
    }

    public void ReportUnexpectedToken(TextLocation location, Token actual, TokenType expectedKind)
    {
        ReportError(location, $"需要<{expectedKind}>但是意外的<{actual.Value}>", DiagnosticCodes.UnexpectedToken);
    }

    public void ReportInvalidImport(TextLocation location, Token modToken)
    {
        ReportError(location, $"导入库不存在 ：{modToken.Value}", DiagnosticCodes.InvalidImport);
    }

    public void ReportInvalidExpressionStatement(TextLocation location)
    {
        ReportError(location, "无效的表达式语句", DiagnosticCodes.InvalidExpressionStatement);
    }

    public void ReportInvalidKeyActionStatement(TextLocation location, Token actual)
    {
        ReportError(location, $"按键语法不正确 ：{actual}", DiagnosticCodes.InvalidKeyActionStatement);
    }

    public void ReportInvalidEOF(TextLocation location, Token actual)
    {
        ReportError(location, $"期望结束但多余的 ：{actual.Value}", DiagnosticCodes.InvalidEOF);
    }

    public void ReportTooMuchLoop(TextLocation location)
    {
        _diagnostics.Add(Diagnostic.Warning(location, "循环层数过多，请优化脚本"));
        ReportError(location, "循环层数过多，请优化脚本", DiagnosticCodes.TooMuchLoop);
    }

    public void ReportInvalidBreakOrContinue(TextLocation location, Token keyword)
    {
        ReportError(location, $"跳出语句 {keyword.Value} 循环层数不足", DiagnosticCodes.InvalidBreakOrContinue);
    }

    public void ReportBadStruct(TextLocation location, string message)
    {
        ReportError(location, message, DiagnosticCodes.BadStruct);
    }

    public void ReportAllPathsMustReturn(TextLocation location)
    {
        ReportError(location, "函数所有路径必须有返回值", DiagnosticCodes.AllPathsMustReturn);
    }

    public void ReportParameterAlreadyDeclared(TextLocation location, string paramName)
    {
        ReportError(location, $"重复定义的参数名: {paramName}", DiagnosticCodes.ParameterAlreadyDeclared);
    }

    public void ReportCannotConvert(TextLocation location, ScriptType fromType, ScriptType toType)
    {
        ReportError(location, $"类型不匹配：无法将 {fromType} 转换成 {toType}", DiagnosticCodes.CannotConvert);
    }

    public void ReportVoidFunctionCannotReturn(TextLocation location, FunctionSymbol function, ScriptType type)
    {
        ReportError(location, $"函数 '{function.Name}' 返回类型为 void，不应有返回值", DiagnosticCodes.VoidFunctionCannotReturn);
    }

    public void ReportFunctionMustReturnValue(TextLocation location, FunctionSymbol function, ScriptType returnType)
    {
        ReportError(location, $"函数 '{function.Name}' 必须返回类型为 {returnType} 的值", DiagnosticCodes.FunctionMustReturnValue);
    }

    public void ReportInvalidConstantExpression(TextLocation location)
    {
        ReportError(location, "常量表达式不正确", DiagnosticCodes.InvalidConstantExpression);
    }

    public void ReportConstantAlreadyDefined(Token constantToken)
    {
        ReportError(constantToken.Location, $"重复定义的常量 '{constantToken.Value}'", DiagnosticCodes.ConstantAlreadyDefined);
    }

    public void ReportCannotAssignToSpecialConstant(Token constantToken)
    {
        ReportError(constantToken.Location, $"特殊常量 '{constantToken.Value}' 不能被赋值", DiagnosticCodes.CannotAssignToSpecialConstant);
    }

    public void ReportUnsupportedBinaryOperator(TextLocation location, Token opToken, ScriptType leftType, ScriptType rightType)
    {
        ReportError(location, $"不支持的运算符:{opToken.Value}对于类型 <{leftType}>和<{rightType}>", DiagnosticCodes.UnsupportedBinaryOperator);
    }

    public void ReportUnsupportedUnaryOperator(TextLocation location, Token opToken, ScriptType operandType)
    {
        ReportError(location, $"不支持的运算符:{opToken.Value}对于类型 <{operandType}>", DiagnosticCodes.UnsupportedUnaryOperator);
    }

    public void ReportVoidExpressionCannotAssign(TextLocation location)
    {
        ReportError(location, "空值表达式无法赋值", DiagnosticCodes.VoidExpressionCannotAssign);
    }

    public void ReportReadOnlyVariable(Token variableToken)
    {
        ReportError(variableToken.Location, $"只读变量无法修改：{variableToken.Value}", DiagnosticCodes.ReadOnlyVariable);
    }

    public void ReportUnknownType(TextLocation location, Token typeToken)
    {
        ReportError(location, $"未知类型：{typeToken.Value}", DiagnosticCodes.UnknownType);
    }

    public void ReportFunctionNotFound(TextLocation location, string functionName)
    {
        ReportError(location, $"找不到调用函数 {functionName}", DiagnosticCodes.FunctionNotFound);
    }

    public void ReportUnknownExpressionType(TextLocation location)
    {
        ReportError(location, "未知的表达式类型", DiagnosticCodes.UnknownExpressionType);
    }

    public void ReportImageLabelNotFound(TextLocation location, string labelName)
    {
        ReportError(location, $"找不到识图标签\"@{labelName}\"", DiagnosticCodes.ImageLabelNotFound);
    }

    public void ReportArrayElementTypeMismatch(TextLocation location)
    {
        ReportError(location, "数组成员类型必须一致", DiagnosticCodes.ArrayElementTypeMismatch);
    }

    public void ReportTypeDoesNotSupportIndexAccess(TextLocation location, ScriptType type)
    {
        ReportError(location, $"类型 <{type}> 不支持索引访问", DiagnosticCodes.TypeDoesNotSupportIndexAccess);
    }

    public void ReportTypeDoesNotSupportSlice(TextLocation location, ScriptType type)
    {
        ReportError(location, $"类型 <{type}> 不支持切片操作", DiagnosticCodes.TypeDoesNotSupportSlice);
    }

    public void ReportVariableNotFound(TextLocation location, string varName)
    {
        ReportError(location, $"找不到变量 {varName}", DiagnosticCodes.VariableNotFound);
    }

    public void ReportFunctionArgumentCountMismatch(TextLocation location, FunctionSymbol fn)
    {
        ReportError(location, $"函数 {fn.Name} 参数数量不匹配", DiagnosticCodes.FunctionArgumentCountMismatch);
    }

    public void ReportFunctionAlreadyDeclared(TextLocation location, string functionName)
    {
        ReportError(location, $"重复定义的函数: {functionName}", DiagnosticCodes.FunctionAlreadyDeclared);
    }

    public void ReportAmbiguousCall(TextLocation location, string functionName, FunctionSymbol[] candidates)
    {
        var sigs = string.Join(", ", candidates.Select(c =>
            $"{c.Name}({string.Join(", ", c.Parameters.Select(p => p.Type.Name))})"));
        ReportError(location, $"函数调用 '{functionName}' 存在歧义，匹配的重载: {sigs}", DiagnosticCodes.AmbiguousCall);
    }

    public void ReportNoMatchingOverload(TextLocation location, string functionName, ScriptType[] argTypes)
    {
        var args = string.Join(", ", argTypes.Select(t => t.Name));
        ReportError(location, $"找不到匹配的函数 '{functionName}'，参数类型: ({args})", DiagnosticCodes.NoMatchingOverload);
    }

    public void ReportNamespaceNotFound(TextLocation location, string name)
    {
        ReportError(location, $"命名空间 '{name}' 不存在", DiagnosticCodes.NamespaceNotFound);
    }

    public void ReportFunctionNotFoundInNamespace(TextLocation location, string funcName, string nsName)
    {
        ReportError(location, $"命名空间 '{nsName}' 中不存在函数 '{funcName}'", DiagnosticCodes.FunctionNotFoundInNamespace);
    }

    public void ReportNamespaceConflictsWithFunction(TextLocation location, string name)
    {
        ReportError(location, $"命名空间 '{name}' 与已声明的函数名冲突", DiagnosticCodes.NamespaceConflictsWithFunction);
    }

    public void ReportCircularImport(TextLocation location, string path)
    {
        ReportError(location, $"循环导入: {Path.GetFileName(path)}", DiagnosticCodes.CircularImport);
    }

    public void ReportImportFileNotFound(TextLocation location, string path)
    {
        ReportError(location, $"导入文件不存在: {path}", DiagnosticCodes.ImportFileNotFound);
    }

    public void ReportModuleNameConflict(TextLocation location, string moduleName, string path)
    {
        ReportError(location, $"模块名冲突: {moduleName}（{path}）", DiagnosticCodes.ModuleNameConflict);
    }
}