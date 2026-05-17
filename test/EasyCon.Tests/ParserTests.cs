using EasyCon.Script.Syntax;

namespace EasyCon.Tests;

/// <summary>
/// 纯语法层测试：只验证解析成功/失败，不涉及类型检查或执行。
/// 类型检查 → BinderTests，运行时行为 → EvaluatorTests。
/// </summary>
[TestFixture]
public class ParserTests
{
    private static (SyntaxTree Tree, bool Success, List<string> Errors) Parse(string code)
    {
        var tree = SyntaxTree.Parse(code);
        var errors = tree.Diagnostics.Where(d => d.IsError).Select(d => $"line {d.Location.StartLine}:{d.Message}").ToList();
        return (tree, errors.Count == 0, errors);
    }

    private static void ExpectParse(string code)
    {
        var (_, success, errors) = Parse(code);
        Assert.That(success, Is.True, $"Expected parse success but got: {string.Join("; ", errors)}");
    }

    private static void ExpectError(string code, string? contains = null)
    {
        var (_, success, errors) = Parse(code);
        Assert.That(success, Is.False, "Expected parse error but got success");
        if (contains != null)
            Assert.That(errors.Any(e => e.Contains(contains)), Is.True,
                $"Expected error containing '{contains}' but got: {string.Join("; ", errors)}");
    }

    #region 空白和注释

    [Test]
    public void EmptyInput()
    {
        ExpectParse("");
    }

    [Test]
    public void OnlyComments()
    {
        ExpectParse("# comment\n# another");
    }

    [Test]
    public void InlineComment()
    {
        ExpectParse("A # inline comment");
    }

    #endregion

    #region 按键语句

    [Test]
    public void KeyPress_AllButtons()
    {
        ExpectParse(@"
A
B
X
Y
L
R
ZL
ZR
MINUS
PLUS
LCLICK
RCLICK
HOME
CAPTURE");
    }

    [Test]
    public void KeyPress_WithDuration()
    {
        ExpectParse("A 500");
    }

    [Test]
    public void KeyPress_WithVariableDuration()
    {
        ExpectParse("$t = 500\nA $t");
    }

    [Test]
    public void KeyPress_WithState()
    {
        ExpectParse("A UP\nA DOWN");
    }

    [Test]
    public void KeyPress_DirectionAsButton()
    {
        ExpectParse("UP\nDOWN\nLEFT\nRIGHT");
    }

    #endregion

    #region 摇杆语句

    [Test]
    public void Stick_Direction()
    {
        ExpectParse("LS RIGHT\nLS UP\nLS DOWN\nLS LEFT");
    }

    [Test]
    public void Stick_WithDuration()
    {
        ExpectParse("LS RIGHT,500\nRS DOWN,2000");
    }

    [Test]
    public void Stick_Degree()
    {
        ExpectParse("LS 135\nLS 180,1000");
    }

    [Test]
    public void Stick_Reset()
    {
        ExpectParse("LS RESET\nRS RESET");
    }

    #endregion

    #region WAIT 语句

    [Test]
    public void Wait_Literal()
    {
        ExpectParse("WAIT 500");
    }

    [Test]
    public void Wait_Variable()
    {
        ExpectParse("$t = 500\nWAIT $t");
    }

    [Test]
    public void Wait_BareNumber()
    {
        // 裸数字被解析为 WAIT
        ExpectParse("500");
    }

    #endregion

    #region PRINT / ALERT

    [Test]
    public void Print_Text()
    {
        ExpectParse("PRINT hello world");
    }

    [Test]
    public void Print_Concatenation()
    {
        ExpectParse("PRINT hello & $count & !");
    }

    [Test]
    public void Alert_Text()
    {
        ExpectParse("ALERT warning");
    }

    [Test]
    public void Alert_Variable()
    {
        ExpectParse("ALERT $msg");
    }

    #endregion

    #region 变量和常量

    [Test]
    public void Variable_Assignment()
    {
        ExpectParse("$x = 10\n$y = $x + 1");
    }

    [Test]
    public void Variable_AugmentedAssignment()
    {
        ExpectParse("$v = 0\n$v += 1\n$v -= 2\n$v *= 3\n$v /= 4\n$v %= 5\n$v &= 6\n$v |= 7\n$v ^= 8\n$v <<= 9\n$v >>= 10");
    }

    [Test]
    public void Constant_Definition()
    {
        ExpectParse("_PI = 3.14\n_RADIUS = 10");
    }

    [Test]
    public void Constant_WithExpression()
    {
        ExpectParse("_SUM = 1 + 2 + 3");
    }

    [Test]
    public void Constant_Redefinition()
    {
        // 常量可以重复定义（最后一定义生效）
        ExpectParse("_X = 1\n_X = 2");
    }

    #endregion

    #region 变量赋值类型标注

    [Test]
    public void Variable_AssignmentWithTypeAnnotation()
    {
        ExpectParse("$x:int = 10");
        ExpectParse("$y:string = \"hello\"");
        ExpectParse("$z:double = 3.14");
    }

    [Test]
    public void Variable_AssignmentWithTypeAnnotationAndAugmentedAssign()
    {
        ExpectParse("$v:int = 0\n$v += 1");
    }

    [Test]
    public void Variable_AssignmentWithTypeAnnotation_FieldAccess_Error()
    {
        // 字段访问不支持类型标注
        ExpectError("$obj.field:int = 10");
    }

    [Test]
    public void Variable_AssignmentWithTypeAnnotation_ArrayIndex_Error()
    {
        // 数组索引不支持类型标注
        ExpectError("$arr[0]:int = 10");
    }

    [Test]
    public void Variable_AssignmentWithArrayTypeAnnotation_Error()
    {
        // 变量赋值不支持定长数组类型标注
        ExpectError("$x:int[10] = [1,2,3,4,5,6,7,8,9,10]");
    }

    [Test]
    public void Error_TypedAssignment_MissingValue()
    {
        ExpectError("$var:int =");
    }

    [Test]
    public void Error_TypedAssignment_AugmentedMissingValue()
    {
        ExpectError("$var:int += ");
    }

    [Test]
    public void Error_TypeAnnotation_MissingTypeName()
    {
        // 冒号后缺类型名
        ExpectError("$var: = 1");
    }

    [Test]
    public void Error_TypeAnnotation_EmptyAfterColon()
    {
        ExpectError("$var:");
    }

    [Test]
    public void Error_ArrayTypeAnnotation_NonIntegerSize()
    {
        // 数组大小必须是整数
        ExpectError("$var:int[abc]");
    }

    [Test]
    public void Error_ArrayTypeAnnotation_EmptyBrackets()
    {
        ExpectError("$var:int[]");
    }

    [Test]
    public void Error_PlainAssignment_MissingValue()
    {
        ExpectError("$var = ");
    }

    [Test]
    public void Error_PlainAssignment_NoOperator()
    {
        // 只有变量没有赋值操作符
        ExpectError("$var");
    }

    #endregion

    #region 表达式 — 字面量和标识符

    [Test]
    public void Expr_Literals()
    {
        ExpectParse("$v = 42");
        ExpectParse("$v = 3.14");
        ExpectParse("$v = \"hello\"");
    }

    [Test]
    public void Expr_Variables()
    {
        ExpectParse("$v = $a");
        ExpectParse("$v = _CONST");
        ExpectParse("$v = @ext");
    }

    #endregion

    #region 表达式 — 运算符

    [Test]
    public void Expr_ArithmeticOperators()
    {
        ExpectParse("$v = 1 + 2");
        ExpectParse("$v = 3 - 4");
        ExpectParse("$v = 5 * 6");
        ExpectParse("$v = 8 / 2");
        ExpectParse("$v = 10 % 3");
        ExpectParse("$v = 7 \\ 2");
    }

    [Test]
    public void Expr_ComparisonOperators()
    {
        ExpectParse("$v = 1 == 2");
        ExpectParse("$v = 1 != 2");
        ExpectParse("$v = 3 > 2");
        ExpectParse("$v = 1 < 2");
        ExpectParse("$v = 3 >= 2");
        ExpectParse("$v = 1 <= 2");
        ExpectParse("$v = \"bc\" in \"abcd\"");
        ExpectParse("$v = 2 in [1, 2, 3]");
    }

    [Test]
    public void Expr_BitwiseOperators()
    {
        ExpectParse("$v = 5 & 3");
        ExpectParse("$v = 5 | 3");
        ExpectParse("$v = 5 ^ 3");
        ExpectParse("$v = ~5");
        ExpectParse("$v = 1 << 2");
        ExpectParse("$v = 4 >> 1");
    }

    [Test]
    public void Expr_LogicalOperators()
    {
        ExpectParse("$v = 1 and 0");
        ExpectParse("$v = 1 or 0");
        ExpectParse("$v = not 0");
    }

    [Test]
    public void Expr_UnaryMinus()
    {
        ExpectParse("$v = -1");
        ExpectParse("$v = -$a");
        ExpectParse("$v = -($a + $b)");
    }

    #endregion

    #region 表达式 — 优先级和结合性

    [Test]
    public void Precedence_MulBeforeAdd()
    {
        // 1 + 2 * 3 应该解析为 1 + (2 * 3)
        ExpectParse("$v = 1 + 2 * 3");
    }

    [Test]
    public void Precedence_ComparisonBelowArithmetic()
    {
        ExpectParse("$v = 1 + 2 > 3");
    }

    [Test]
    public void Precedence_AndBelowComparison()
    {
        ExpectParse("$v = 1 > 0 and 2 < 3");
        ExpectParse("$v = \"bc\" in \"abcd\" and 1 == 1");
    }

    [Test]
    public void Precedence_OrBelowAnd()
    {
        ExpectParse("$v = 1 > 0 or 2 < 3");
    }

    [Test]
    public void Precedence_FullChain()
    {
        // or < and < 比较 < +/- < */ < 一元
        ExpectParse("$v = 1 + 2 * 3 and 4 == 5");
        ExpectParse("$v = (1 + 2) * (3 - 4) or $a > $b");
    }

    [Test]
    public void Parenthesized_OverridesPrecedence()
    {
        ExpectParse("$v = (1 + 2) * 3");
        ExpectParse("$v = ((1 + 2) * (3 - 4))");
    }

    [Test]
    public void Associativity_LeftToRight()
    {
        ExpectParse("$v = 1 + 2 + 3 + 4");
        ExpectParse("$v = 2 * 3 / 4 % 5");
    }

    #endregion

    #region 表达式 — 函数调用

    [Test]
    public void Call_NoArgs()
    {
        ExpectParse("$v = TIME()");
    }

    [Test]
    public void Call_WithArgs()
    {
        ExpectParse("$v = RAND(10)");
        ExpectParse("$v = RAND($max)");
    }

    [Test]
    public void Call_NestedInExpression()
    {
        ExpectParse("$v = RAND(10) + 1");
    }

    [Test]
    public void Call_MultipleArgs()
    {
        ExpectParse("BEEP 440, 1000");
    }

    #endregion

    #region 表达式 — 数组和索引

    [Test]
    public void Array_Literal()
    {
        ExpectParse("$a = [1, 2, 3]");
    }

    [Test]
    public void Array_Empty()
    {
        ExpectParse("$a = []");
    }

    [Test]
    public void Array_Index()
    {
        ExpectParse("$a = [1,2,3]\n$r = $a[0]");
    }

    [Test]
    public void Array_Slice()
    {
        ExpectParse("$a = [1,2,3]\n$r = $a[1:3]");
    }

    [Test]
    public void Lvalue_SliceIsError()
    {
        ExpectError("$a[1:3] = 5");
    }

    [Test]
    public void String_Index()
    {
        ExpectParse("$s = \"abc\"\n$r = $s[1]");
    }

    #endregion

    #region 条件分支 IF

    [Test]
    public void If_Simple()
    {
        ExpectParse("IF $x == 1\nA\nENDIF");
    }

    [Test]
    public void If_Else()
    {
        ExpectParse("IF $x == 1\nA\nELSE\nB\nENDIF");
    }

    [Test]
    public void If_Elif()
    {
        ExpectParse("IF $x == 1\nA\nELIF $x == 2\nB\nENDIF");
    }

    [Test]
    public void If_ElifElse()
    {
        // ELIF + ELSE 组合是已知的 parser 限制
        ExpectParse("IF $x == 1\nA\nELIF $x == 2\nB\nELSE\nPRINT OK\nENDIF");
    }

    [Test]
    public void If_Nested()
    {
        ExpectParse("IF $x == 1\nIF $y == 2\nA\nENDIF\nENDIF");
    }

    #endregion

    #region 循环 FOR

    [Test]
    public void For_Count()
    {
        ExpectParse("FOR 3\nA\nNEXT");
    }

    [Test]
    public void For_VariableCount()
    {
        ExpectParse("$n = 5\nFOR $n\nA\nNEXT");
    }

    [Test]
    public void For_Range()
    {
        ExpectParse("FOR $i = 1 TO 10\nA\nNEXT");
    }

    [Test]
    public void For_Nested()
    {
        ExpectParse("FOR 3\nFOR 2\nA\nNEXT\nNEXT");
    }

    #endregion

    #region 循环 WHILE

    [Test]
    public void While_Basic()
    {
        ExpectParse("WHILE $i < 10\n$i += 1\nEND");
    }

    [Test]
    public void While_Nested()
    {
        ExpectParse("WHILE $a < 5\nWHILE $b < 3\nA\nEND\nEND");
    }

    #endregion

    #region 循环 UNTIL

    [Test]
    public void Until_Basic()
    {
        ExpectParse("UNTIL $i >= 10\n$i += 1\nEND");
    }

    [Test]
    public void Until_Nested()
    {
        ExpectParse("UNTIL $a >= 5\nUNTIL $b >= 3\nA\nEND\nEND");
    }

    #endregion

    #region 循环控制 BREAK / CONTINUE

    [Test]
    public void Break_Simple()
    {
        ExpectParse("FOR 3\nBREAK\nNEXT");
    }

    [Test]
    public void Break_WithLevel()
    {
        ExpectParse("FOR 3\nFOR 3\nBREAK 2\nNEXT\nNEXT");
    }

    [Test]
    public void Continue_Simple()
    {
        ExpectParse("FOR 3\nCONTINUE\nA\nNEXT");
    }

    #endregion

    #region 函数 FUNC

    [Test]
    public void Func_Void()
    {
        ExpectParse("FUNC greet\nA\nENDFUNC");
    }

    [Test]
    public void Func_WithReturnType()
    {
        ExpectParse("FUNC add($a, $b) : int\nRETURN $a + $b\nENDFUNC");
    }

    [Test]
    public void Func_WithArrayParameterAndReturnType()
    {
        ExpectParse("FUNC first($items:STRING[]):STRING\nRETURN $items[0]\nENDFUNC");
        ExpectParse("FUNC copy($items:STRING[]):STRING[]\nRETURN $items\nENDFUNC");
    }

    [Test]
    public void Func_FixedLengthArrayParameter_NotSupported()
    {
        ExpectError("FUNC first($items:STRING[4]):STRING\nRETURN $items[0]\nENDFUNC");
    }

    [Test]
    public void Func_ChineseName()
    {
        ExpectParse("FUNC 测试函数\nA\nENDFUNC");
    }

    [Test]
    public void Func_ReturnVoid()
    {
        ExpectParse("FUNC noop\nRETURN\nENDFUNC");
    }

    [Test]
    public void Func_ReturnExpr()
    {
        ExpectParse("FUNC get\nRETURN 42\nENDFUNC");
    }

    [Test]
    public void Func_ForwardReference()
    {
        // 函数可以在定义前调用
        ExpectParse("CALL foo\nFUNC foo\nA\nENDFUNC");
    }

    #endregion

    #region EXTERN 声明

    [Test]
    public void Extern_Declaration()
    {
        ExpectParse("EXTERN FUNC Sleep($ms:INT):VOID FROM \"kernel32.dll\"");
    }

    [Test]
    public void Extern_WithAsClause()
    {
        ExpectParse("EXTERN FUNC mysqrt($x:DOUBLE):DOUBLE AS \"sqrt\" FROM \"msvcrt.dll\"");
    }

    #endregion

    #region AMIIBO

    [Test]
    public void Amiibo_Literal()
    {
        ExpectParse("AMIIBO 3");
    }

    [Test]
    public void Amiibo_Variable()
    {
        ExpectParse("$n = 5\nAMIIBO $n");
    }

    #endregion

    #region 外部变量 @

    [Test]
    public void ExternVar_InExpression()
    {
        ExpectParse("$2 = @5号路蛋屋主人\nIF $2 > 95\nPRINT $2\nENDIF");
    }

    #endregion

    #region 综合场景

    [Test]
    public void Complex_PrimeSieve()
    {
        ExpectParse(@"FOR $n = 2 TO 100
    $isPrime = 1
    FOR $d = 2 TO $n
        IF $n % $d == 0
            $isPrime = 0
            BREAK
        ENDIF
    NEXT
    IF $isPrime == 1
        PRINT $n
    ENDIF
NEXT");
    }

    [Test]
    public void Complex_FunctionWithLoop()
    {
        ExpectParse(@"FUNC loop
FOR 3
A
500
NEXT
ENDFUNC
CALL loop");
    }

    [Test]
    public void Complex_MixedStatements()
    {
        ExpectParse(@"
# 脚本注释
$val = 10
PRINT $val
FOR 5
    A
    WAIT 100
NEXT
IF $val > 5
    PRINT big
ELIF $val == 5
    PRINT equal
ELSE
    PRINT small
ENDIF
");
    }

    #endregion

    #region 错误 — 结构性错误

    [Test]
    public void Error_MismatchedIf_MissingEndif()
    {
        ExpectError("IF $x == 1\nA", "没有正确结束");
    }

    [Test]
    public void Error_MismatchedFor_MissingNext()
    {
        ExpectError("FOR 3\nA", "没有正确结束");
    }

    [Test]
    public void Error_MismatchedFunc_MissingEndfunc()
    {
        ExpectError("FUNC foo\nA", "没有正确结束");
    }

    [Test]
    public void Error_MismatchedWhile_MissingEnd()
    {
        ExpectError("WHILE $i < 5\nA", "没有正确结束");
    }

    [Test]
    public void Error_MismatchedUntil_MissingEnd()
    {
        ExpectError("UNTIL $i >= 5\nA", "没有正确结束");
    }

    [Test]
    public void Error_ExtraEndif()
    {
        ExpectError("ENDIF", "多余的");
    }

    [Test]
    public void Error_ExtraNext()
    {
        ExpectError("NEXT", "多余的");
    }

    [Test]
    public void Error_ElifWithoutIf()
    {
        ExpectError("ELIF $x == 1\nA", "ELIF");
    }

    [Test]
    public void Error_ElseWithoutIf()
    {
        ExpectError("ELSE\nA", "ELSE");
    }

    [Test]
    public void Error_ElseAfterElse()
    {
        ExpectError("IF $x == 1\nA\nELSE\nB\nELSE\nC\nENDIF", "一个If只能对应一个Else");
    }

    #endregion

    #region 错误 — 表达式错误

    [Test]
    public void Error_TrailingOperator()
    {
        ExpectError("$v = 1 +");
        ExpectError("$v = 1 -");
        ExpectError("$v = 1 *");
        ExpectError("$v = 1 /");
        ExpectError("$v = 1 %");
    }

    [Test]
    public void Error_TrailingComparison()
    {
        ExpectError("$v = 1 ==");
        ExpectError("$v = 1 !=");
        ExpectError("$v = 1 >");
        ExpectError("$v = 1 <");
    }

    [Test]
    public void Error_TrailingLogical()
    {
        ExpectError("$v = 1 and");
        ExpectError("$v = 1 or");
    }

    [Test]
    public void Error_TrailingBitwise()
    {
        ExpectError("$v = 1 &");
        ExpectError("$v = 1 |");
        ExpectError("$v = 1 ^");
        ExpectError("$v = 1 <<");
        ExpectError("$v = 1 >>");
    }

    [Test]
    public void Error_MultipleTrailingOperators()
    {
        ExpectError("$v = 1 + *");
        ExpectError("$v = 1 * /");
    }

    [Test]
    public void Error_UnmatchedParen()
    {
        ExpectError("$v = (1 + 2");
    }

    [Test]
    public void Error_ExpressionInIf()
    {
        ExpectError("IF $a +\nA\nENDIF");
        ExpectError("IF $a ==\nA\nENDIF");
    }

    [Test]
    public void Error_ExpressionInFor()
    {
        ExpectError("FOR $a = 1 +\nNEXT");
    }

    [Test]
    public void Error_AugmentedAssignmentTrailing()
    {
        ExpectError("$v += ");
        ExpectError("$v -= ");
        ExpectError("$v *= ");
    }

    #endregion

    #region 错误 — 词法错误传播

    [Test]
    public void Error_UnterminatedString()
    {
        ExpectError("$s=\"unterminated", "字符串没有结束引号");
    }

    [Test]
    public void Error_InvalidToken()
    {
        ExpectError("FOR 3\nA\nNEXT\nEXTRA", "无效的表达式语句");
    }

    [Test]
    public void Error_BadCharacter()
    {
        ExpectError("$val = 10 @", "多余的");
    }

    #region 赋值目标

    [Test]
    public void Assign_ArrayIndex()
    {
        ExpectParse("$arr[0] = 10");
    }

    [Test]
    public void Assign_ArrayIndex_Augmented()
    {
        ExpectParse("$arr[0] += 5");
    }

    [Test]
    public void Assign_FieldAccess()
    {
        ExpectParse("$obj.field = 10");
    }

    [Test]
    public void Assign_ChainedFieldIndex()
    {
        ExpectParse("$obj.arr[0] = 10");
    }

    [Test]
    public void Assign_Discard()
    {
        ExpectParse("_ = 42");
    }

    #endregion

    #endregion
}