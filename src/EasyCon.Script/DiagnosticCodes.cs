namespace EasyCon.Script;

/// <summary>
/// 诊断错误码分配表（M1 诊断结构化，SCRIPT_MODERNIZATION_PLAN.md；单一事实源）。
/// 分段：0001–0099 词法/语法，0101–0199 声明与绑定，0201–0299 类型，
/// 0301–0399 字节码/容器，0401–0499 模块系统。新增诊断在对应段尾追加，不复用已撤码。
/// </summary>
public static class DiagnosticCodes
{
    // ---- 词法/语法（DiagnosticBag.Report* ← Lexer/Parser）----
    public const string BadCharacter = "ECX0001";
    public const string InvalidNumber = "ECX0002";
    public const string UnterminatedString = "ECX0003";
    public const string UnexpectedToken = "ECX0004";
    public const string InvalidImport = "ECX0005";
    public const string InvalidExpressionStatement = "ECX0006";
    public const string InvalidKeyActionStatement = "ECX0007";
    public const string InvalidEOF = "ECX0008";
    public const string TooMuchLoop = "ECX0009";
    public const string InvalidBreakOrContinue = "ECX0010";
    public const string BadStruct = "ECX0011";

    // ---- 声明与绑定 ----
    public const string VariableNotFound = "ECX0101";
    public const string FunctionNotFound = "ECX0102";
    public const string FunctionAlreadyDeclared = "ECX0103";
    public const string ParameterAlreadyDeclared = "ECX0104";
    public const string ConstantAlreadyDefined = "ECX0105";
    public const string CannotAssignToSpecialConstant = "ECX0106";
    public const string ReadOnlyVariable = "ECX0107";
    public const string UnknownType = "ECX0108";
    public const string NamespaceNotFound = "ECX0109";
    public const string FunctionNotFoundInNamespace = "ECX0110";
    public const string NamespaceConflictsWithFunction = "ECX0111";
    public const string CircularImport = "ECX0112";
    public const string ImportFileNotFound = "ECX0113";
    public const string ModuleNameConflict = "ECX0114";
    public const string ImageLabelNotFound = "ECX0115";
    public const string InvalidConstantExpression = "ECX0116";

    // ---- 类型 ----
    public const string CannotConvert = "ECX0201";
    public const string UnsupportedBinaryOperator = "ECX0202";
    public const string UnsupportedUnaryOperator = "ECX0203";
    public const string VoidExpressionCannotAssign = "ECX0204";
    public const string AllPathsMustReturn = "ECX0205";
    public const string VoidFunctionCannotReturn = "ECX0206";
    public const string FunctionMustReturnValue = "ECX0207";
    public const string ArrayElementTypeMismatch = "ECX0208";
    public const string TypeDoesNotSupportIndexAccess = "ECX0209";
    public const string TypeDoesNotSupportSlice = "ECX0210";
    public const string UnknownExpressionType = "ECX0211";
    public const string AmbiguousCall = "ECX0212";
    public const string NoMatchingOverload = "ECX0213";
    public const string FunctionArgumentCountMismatch = "ECX0214";

    // ---- 字节码/容器（异常包装）----
    public const string BytecodeEmit = "ECX0301";
    public const string ContainerImage = "ECX0302";

    // ---- 模块系统 ----
    public const string ModuleCompileFailed = "ECX0401";
    public const string DependencyFailed = "ECX0402";       // M2 级联：依赖模块编译失败
    public const string ErrorCacheReplay = "ECX0403";       // 错误缓存重放（§7.3）
}
