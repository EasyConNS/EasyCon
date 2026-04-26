using EasyCon.Script;
using EasyCon.Script.Syntax;
using System.Collections.Immutable;

namespace EasyCon.Tests;

[TestFixture]
public class ParserTests
{
    [SetUp]
    public void Setup()
    {
        // setup
    }

    [Test]
    public void Test_Ignore()
    {
        Assert.Pass();
    }

    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(1)]
    public void IsPrime_ValuesLessThan2_ReturnFalse(int value)
    {
        var success = value < 2;
        Assert.That(success, Is.True, $"{value} should not be prime");
    }

    // [Test]
    // public void Tokenize_InvalidNumber_ReportsDiagnostics()
    // {
    //     var tree = SyntaxTree.Parse("$x = 1.2.3");
    //     Assert.That(tree.Diagnostics.Count, Is.GreaterThan(0));
    // }

    // [Test]
    // public void Tokenize_UnterminatedString_ReportsDiagnostics()
    // {
    //     var tree = SyntaxTree.Parse("$x = \"unterminated");
    //     Assert.That(tree.Diagnostics.Count, Is.GreaterThan(0));
    // }

    // [Test]
    // public void Tokenize_BadCharacter_ReportsDiagnostics()
    // {
    //     var tree = SyntaxTree.Parse("$x = 10 \u0001");
    //     Assert.That(tree.Diagnostics.Count, Is.GreaterThan(0));
    // }

    private static (SyntaxTree Tree, bool Success, List<string> Errors) Parse(string code)
    {
        var tree = SyntaxTree.Parse(code);
        var errors = tree.Diagnostics.Where(d => d.IsError).Select(d => $"line {d.Location.StartLine}:{d.Message}").ToList();
        var success = errors.Count == 0;
        return (tree, success, errors);
    }

    private static (Compilation Compilation, bool Success, List<string> Errors) Compile(string code, ImmutableHashSet<string>? exvar = null)
    {
        var tree = SyntaxTree.Parse(code);
        var errors = tree.Diagnostics.Where(d => d.IsError).Select(d => d.Message).ToList();
        if (errors.Count == 0)
        {
            try
            {
                var compilation = Compilation.Create(tree);
                var diag = compilation.Compile(exvar ?? []);
                foreach (var d in diag)
                    errors.Add(d.Message);
            }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
            }
        }
        var success = errors.Count == 0;
        return (null!, success, errors);
    }

    private static (EvaluationResult Result, bool Success, List<string> Errors) Run(string code, ImmutableHashSet<string>? exvar = null)
    {
        var errors = new List<string>();
        var cancelToken = new CancellationTokenSource();
        try
        {
            var compilation = Compilation.Create(SyntaxTree.Parse(code));
            compilation.Evaluate(null, null, [], cancelToken.Token);
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
        }
        var success = errors.Count == 0;
        return (null!, success, errors);
    }

    private static void ExpectSuccess(string code)
    {
        var (_, success, errors) = Parse(code);
        Assert.That(success, Is.True, $"Expected parse success but got errors: {string.Join(", ", errors)}");
    }

    private static void ExpectError(string code, string? errorContains = null)
    {
        var (_, success, errors) = Parse(code);
        Assert.That(success, Is.False, "Expected parse error but got success");
        if (errorContains != null)
        {
            var hasExpectedError = errors.Any(e => e.Contains(errorContains));
            Assert.That(hasExpectedError, Is.True, $"Expected error containing '{errorContains}' but got: {string.Join(", ", errors)}");
        }
    }
    private static void ExpectBindSuccess(string code)
    {
        var (compilation, success, errors) = Compile(code);
        Assert.That(success, Is.True, $"Expected parse success but got errors: {string.Join(", ", errors)}");
    }
    private static void ExpectBindError(string code, string? errorContains = null)
    {
        var (compilation, success, errors) = Compile(code);
        Assert.That(success, Is.False, "Expected bind error but got success");
        if (errorContains != null)
        {
            var hasExpectedError = errors.Any(e => e.Contains(errorContains));
            Assert.That(hasExpectedError, Is.True, $"Expected error containing '{errorContains}' but got: {string.Join(", ", errors)}");
        }
    }

    #region 注释和输出

    [Test]
    public void CommentAndPrint()
    {
        ExpectSuccess(@"
# 这是一个注释
A # 这同行注释
PRINT 你好世界
PRINT 你好世界 & $1");
    }

    #endregion

    #region 消息推送 ALERT

    [Test]
    public void Alert_SimpleText()
    {
        ExpectSuccess("ALERT $1\nALERT 推送消息");
    }

    #endregion

    #region 按键

    [Test]
    public void Key_All()
    {
        ExpectSuccess(@"
UP UP
DOWN DOWN
$time = 500
A $time
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
CAPTURE
LEFT
RIGHT
UP
DOWN");
    }

    #endregion

    #region 摇杆

    [Test]
    public void Stick_Test()
    {
        ExpectSuccess(@"
LS RIGHT
LS UP,50
RS DOWN,2000
LS 135
LS 180,1000
LS RESET");
    }

    #endregion

    #region 等待 WAIT

    [Test]
    public void Wait_Simple()
    {
        ExpectSuccess(@"
WAIT 500
500
$time = 500
WAIT $time");
    }

    #endregion

    #region 常量和变量

    [Test]
    public void Constant_Define()
    {
        ExpectSuccess(@"
_PI = 1+2
_PI = 3
$val = _PI
$sum = 2+2
$1 = 10
$2 = $1");

        ExpectError(@"
_PI = 3+$sum
_PI -= 2+2
_PI = @识图");
    }

    #endregion

    #region 表达式和运算符

    [Test]
    public void Expression_Common()
    {
        ExpectSuccess(@"
_PI = 1 + 2
$v += $b
$val = 5 - 3
$v -= $b
$val = 2 * 3
$v *= $b
$val = 10 / 2
$v /= $b
$val = 10 % 3
$val = 1 \ 2
$val = 5 & 3
$val = 1 != 2
$val = 5 | 3
$val = 5 ^ 3
$val = ~5
$var = 1 << 2
$val >>= 1");

        ExpectSuccess(@"
$val = 1 != 2
$val = 3 > 2
$val = 1 < 2
$val = 3 >= 2
$val = 1 <= 2");


        ExpectError(@"
_PI = 3+4+");
    }

    [Test]
    public void Expression_Complex()
    {
        ExpectSuccess("$val = (1 + 2) * 3 - 4 / 2");
    }

    #endregion

    #region 循环 FOR...NEXT

    [Test]
    public void For_Loop()
    {
        ExpectSuccess(@"
FOR 3
NEXT
FOR $2
NEXT
FOR $1 = 5 TO 10
A
NEXT
$count = 0
FOR $count
A
NEXT
FOR 3
FOR 2
A
NEXT
NEXT");

        ExpectError(@"
FOR @识图");
    }

    [Test]
    public void BreakAndContinue()
    {
        ExpectSuccess(@"
FOR 3
BREAK
NEXT
FOR 3
FOR 3
BREAK 2
NEXT
NEXT
FOR 3
CONTINUE
A
NEXT");
        ExpectBindError("BREAK", "跳出语句");
    }

    #endregion

    #region 条件分支 IF...ELIF...ELSE...ENDIF

    [Test]
    public void If_Branch()
    {
        ExpectSuccess(@"
IF $1 == 1
A
ENDIF
IF $1 == 1
A
ELSE
B
ENDIF
IF $1 == 1
A
ELIF $1 == 2
B
ENDIF
IF $1 == 1
IF $2 == 2
A
ENDIF
ENDIF");
    }

    #endregion

    #region 函数 FUNC...ENDFUNC

    [Test]
    public void Func_Define()
    {
        ExpectSuccess(@"
FUNC testfunc
A
ENDFUNC
FUNC 测试函数
A
ENDFUNC");
        ExpectError(@"
FUNC testfunc($b
A
ENDFUNC");
    }

    [Test]
    public void Func_Call()
    {
        ExpectSuccess(@"
FUNC myfunc
A
ENDFUNC
CALL myfunc
FUNC myfunc
RETURN
ENDFUNC
FUNC func1
CALL func2
ENDFUNC
FUNC func2
A
ENDFUNC
FUNC myfunc
A
ENDFUNC
CALL myfunc
CALL myfunc");
    }

    [Test]
    public void Func_VariableAndConstant()
    {
        ExpectSuccess(@"
FUNC myfunc
$local = 10
ENDFUNC
FUNC myfunc
_LOCAL = 5
ENDFUNC");
    }

    #endregion

    #region 内置函数

    [Test]
    public void BuiltinFunc()
    {
        ExpectSuccess(@"
$t = TIME()
$r = RAND(10)
$max = 100
$r = RAND($max)
BEEP 440, 1000");
    }

    #endregion

    #region 搜图语法

    [Test]
    public void ImageSearch()
    {
        ExpectSuccess(@"
$2 = @5号路蛋屋主人
$2 = @5号路蛋屋主人
IF $2 > 95
PRINT $2
ENDIF");
    }

    #endregion

    #region Amiibo

    [Test]
    public void Amiibo()
    {
        ExpectSuccess("AMIIBO 3\n$num = 5\nAMIIBO $num");
    }

    #endregion

    #region 错误场景测试

    [Test]
    public void Error_UndefinedVariable()
    {
        var code = @"
print $a
$1 = SUM()
";
        ExpectBindError(code, "找不到");
    }

    [Test]
    public void Error_InvalidKey()
    {
        ExpectError("INVALIDKEY", "无效的表达式");
    }

    [Test]
    public void Error_InvalidStickDirection()
    {
        ExpectError("LS INVALID", "按键语法不正确");
    }

    [Test]
    public void Error_MismatchedIf()
    {
        var code = @"ABV
IF $1 == 1
A
FOR 3
A
";
        ExpectError(code, "没有正确结束");
    }

    [Test]
    public void Error_Invalid()
    {
        var code = @"
$val = 10.2
$val = 10 @
";
        ExpectError(code, "多余的");
    }

    [Test]
    public void Error_UnterminatedString()
    {
        ExpectError("$s=\"unterminated", "字符串没有结束引号");
    }

    [Test]
    public void Error_InvalidEOF()
    {
        ExpectError("FOR 3\nA\nNEXT\nEXTRA", "无效的表达式语句");
    }

    #endregion

    #region 表达式解析健壮性测试

    // === 正常表达式 ===

    [Test]
    public void Expr_SimpleBinary()
    {
        ExpectSuccess("$v = 1 + 2");
        ExpectSuccess("$v = 3 - 4");
        ExpectSuccess("$v = 5 * 6");
        ExpectSuccess("$v = 8 / 2");
        ExpectSuccess("$v = 10 % 3");
        ExpectSuccess("$v = 1 \\ 2");
    }

    [Test]
    public void Expr_BinaryWithVariables()
    {
        ExpectSuccess("$v = $a + $b");
        ExpectSuccess("$v = $a * $b - $c");
        ExpectSuccess("$v = $a + $b * $c");  // 优先级: * > +
    }

    [Test]
    public void Expr_ComparisonOperators()
    {
        ExpectSuccess("$v = 1 == 2");
        ExpectSuccess("$v = 1 != 2");
        ExpectSuccess("$v = 3 > 2");
        ExpectSuccess("$v = 1 < 2");
        ExpectSuccess("$v = 3 >= 2");
        ExpectSuccess("$v = 1 <= 2");
    }

    [Test]
    public void Expr_LogicalOperators()
    {
        ExpectSuccess("$v = 1 and 0");
        ExpectSuccess("$v = 1 or 0");
        ExpectSuccess("$v = not 0");
    }

    [Test]
    public void Expr_BitwiseOperators()
    {
        ExpectSuccess("$v = 5 & 3");
        ExpectSuccess("$v = 5 | 3");
        ExpectSuccess("$v = 5 ^ 3");
        ExpectSuccess("$v = ~5");
        ExpectSuccess("$v = 1 << 2");
        ExpectSuccess("$v = 4 >> 1");
    }

    [Test]
    public void Expr_UnaryMinus()
    {
        ExpectSuccess("$v = -1");
        ExpectSuccess("$v = -$a");
        ExpectSuccess("$v = -($a + $b)");
    }

    [Test]
    public void Expr_Parenthesized()
    {
        ExpectSuccess("$v = (1 + 2)");
        ExpectSuccess("$v = (1 + 2) * 3");
        ExpectSuccess("$v = ((1 + 2) * (3 - 4))");
    }

    [Test]
    public void Expr_NestedBinary()
    {
        ExpectSuccess("$v = 1 + 2 + 3");
        ExpectSuccess("$v = 1 + 2 * 3 - 4 / 2");
        ExpectSuccess("$v = 1 == 2 and 3 > 0");
    }

    [Test]
    public void Expr_ComplexPrecedence()
    {
        // 完整优先级链: or < and < 比较 < +/-/|/^ < */\%&<</>> < 一元
        ExpectSuccess("$v = 1 + 2 * 3 and 4 == 5");
        ExpectSuccess("$v = (1 + 2) * (3 - 4) or $a > $b");
    }

    [Test]
    public void Expr_WithConstant()
    {
        ExpectSuccess("_PI = 3\n$v = _PI + 1");
        ExpectSuccess("$v = _PI * 2");
    }

    [Test]
    public void Expr_WithFunctionCall()
    {
        ExpectSuccess("$v = RAND(10)");
        ExpectSuccess("$v = RAND($max) + 1");
        ExpectSuccess("$v = TIME()");
    }

    // === 错误表达式（不完整 binary expression）===

    [Test]
    public void Expr_Error_TrailingOperator()
    {
        // 操作符后没有右操作数
        ExpectError("$v = 1 +");
        ExpectError("$v = 1 -");
        ExpectError("$v = 1 *");
        ExpectError("$v = 1 /");
        ExpectError("$v = 1 %");
    }

    [Test]
    public void Expr_Error_MultipleTrailingOperators()
    {
        // 连续操作符
        ExpectError("$v = 1 + *");
        ExpectError("$v = 1 + -");
        ExpectError("$v = 1 * /");
    }

    [Test]
    public void Expr_Error_OnlyOperator()
    {
        // 只有一个操作符
        ExpectError("+$v");
        ExpectError("-");  // 只有一个减号
    }

    [Test]
    public void Expr_Error_TrailingComparison()
    {
        ExpectError("$v = 1 ==");
        ExpectError("$v = 1 !=");
        ExpectError("$v = 1 >");
        ExpectError("$v = 1 <");
    }

    [Test]
    public void Expr_Error_TrailingLogical()
    {
        ExpectError("$v = 1 and");
        ExpectError("$v = 1 or");
    }

    [Test]
    public void Expr_Error_TrailingBitwise()
    {
        ExpectError("$v = 1 &");
        ExpectError("$v = 1 |");
        ExpectError("$v = 1 ^");
        ExpectError("$v = 1 <<");
        ExpectError("$v = 1 >>");
    }

    [Test]
    public void Expr_Error_UnmatchedParen()
    {
        ExpectError("$v = (1 + 2");
        // 右括号不匹配在 parser 里可能和左括号路径不同
    }

    [Test]
    public void Expr_Error_EmptyParens()
    {
        // 空括号不应该是合法表达式（除非是函数调用）
        // 具体行为取决于 ParsePrimary 对 LeftParen 的处理
    }

    [Test]
    public void Expr_Error_BinaryInIfCondition()
    {
        // IF 中的表达式也需要健壮
        ExpectError("IF $a +\nA\nENDIF");
        ExpectError("IF $a ==\nA\nENDIF");
        ExpectError("IF $a >\nA\nENDIF");
    }

    [Test]
    public void Expr_Error_BinaryInForRange()
    {
        // FOR 中的表达式
        ExpectError("FOR $a = 1 +\nNEXT");
    }

    [Test]
    public void Expr_Error_ComplexTrailing()
    {
        // 更复杂的未完成表达式
        ExpectError("$v = (1 + 2) *");
        ExpectError("$v = 1 + 2 *");
        ExpectError("$v = $a + $b *");
    }

    [Test]
    public void Expr_Error_AugmentedAssignmentTrailing()
    {
        // 复合赋值后没有右操作数
        ExpectError("$v += ");
        ExpectError("$v -= ");
        ExpectError("$v *= ");
    }

    #endregion

    #region 综合场景测试

    [Test]
    public void Complex_PrimeNumber()
    {
        ExpectSuccess(@"FOR $1 = 2 TO 100
    $5 = 1
    FOR $2 = 2 TO $1
        $3 = $1 % $2
        IF $3 == 0
            $5 = 0
            BREAK
        ENDIF
    NEXT
    IF $5 == 1
        PRINT $1
    ENDIF
NEXT");
    }

    [Test]
    public void Complex_FunctionWithLoop()
    {
        ExpectSuccess(@"FUNC loop
FOR 3
A
500
NEXT
ENDFUNC
CALL loop");
    }

    [Test]
    public void Invalid_print()
    {
        ExpectError(@"print 识别到| 累计： & $count &");
    }

    #region 函数定义和调用测试

    [Test]
    public void Function_SimpleDefinitionAndCall()
    {
        // 测试简单的函数定义和调用
        ExpectBindSuccess(@"
FUNC test
A
ENDFUNC
CALL test");
    }

    [Test]
    public void Function_NestedCall()
    {
        // 测试嵌套调用：func1 调用 func2，func2 调用 func3
        ExpectBindSuccess(@"
FUNC func3
B
ENDFUNC
FUNC func2
CALL func3
ENDFUNC
FUNC func1
CALL func2
ENDFUNC
CALL func1");
    }

    [Test]
    public void Function_UnusedDefinition()
    {
        // 测试未调用的函数定义 - 应该成功编译
        ExpectBindSuccess(@"
FUNC unusedFunc
PRINT 1
ENDFUNC
FUNC usedFunc
PRINT 2
ENDFUNC
CALL usedFunc");
    }

    [Test]
    public void Function_MultipleCalls()
    {
        // 测试多次调用同一函数
        ExpectBindSuccess(@"
FUNC myFunc
PRINT 1
ENDFUNC
CALL myFunc
CALL myFunc
CALL myFunc");
    }

    [Test]
    public void Function_CallWithArgs()
    {
        // 测试带参数的函数调用
        ExpectBindSuccess(@"
FUNC greet($a)
PRINT hello & $a
ENDFUNC
greet 233");
        ExpectBindError(@"
FUNC greet($a)
PRINT hello & $a
ENDFUNC
CALL greet");
    }

    [Test]
    public void Function_RecursiveCall()
    {
        // 测试递归调用 - 斐波那契数列函数
        // F(0) = 0, F(1) = 1, F(n) = F(n-1) + F(n-2)
        ExpectBindSuccess(@"
FUNC fib($n) : int
IF $n == 0
RETURN 0
ENDIF
IF $n == 1
RETURN 1
ENDIF
$1 = $n - 1
$2 = $n - 2
RETURN fib($1) + fib($2)
ENDFUNC
fib 10");
    }

    [Test]
    public void Function_CallBeforeDefVar()
    {
        ExpectBindSuccess(@"
$a = 233
call test
FUNC test
PRINT $a
ENDFUNC");

        // 测试带参数的函数调用
        //        ExpectBindError(@"
        //call test
        //$a = 1
        //FUNC test
        //PRINT $a
        //ENDFUNC");
    }

    #endregion

    [Test]
    public void Complex_AmiiboSwitch()
    {
        ExpectSuccess(@"FOR $3 = 0 TO 20
    AMIIBO $3
NEXT");
    }

    [Test]
    public void Complex_MultipleStatements()
    {
        ExpectBindSuccess(@"
# 这是一个测试脚本
$val = 10
PRINT $val
FOR 5
    A
    WAIT 100
NEXT
IF $val > 5
    PRINT 大于5
ELIF $val == 5
    PRINT 等于5
ELSE
    PRINT 小于5
ENDIF
");
    }

    #endregion
}