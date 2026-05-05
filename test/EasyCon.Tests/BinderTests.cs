using EasyCon.Script;
using EasyCon.Script.Syntax;
using System.Collections.Immutable;

namespace EasyCon.Tests;

/// <summary>
/// 绑定层测试：类型检查、作用域分析、重载决议、泛型推断。
/// 不执行代码，只验证编译阶段的诊断。
/// </summary>
[TestFixture]
public class BinderTests
{
    private static (bool Success, List<string> Errors) Compile(string code, ImmutableHashSet<string>? exvar = null)
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
        return (errors.Count == 0, errors);
    }

    private static void ExpectBind(string code)
    {
        var (success, errors) = Compile(code);
        Assert.That(success, Is.True, $"Expected bind success but got: {string.Join("; ", errors)}");
    }

    private static void ExpectError(string code, string? contains = null)
    {
        var (success, errors) = Compile(code);
        Assert.That(success, Is.False, "Expected bind error but got success");
        if (contains != null)
            Assert.That(errors.Any(e => e.Contains(contains)), Is.True,
                $"Expected error containing '{contains}' but got: {string.Join("; ", errors)}");
    }

    #region 变量和常量作用域

    [Test]
    public void Variable_DeclareAndUse()
    {
        ExpectBind("$x = 10\n$y = $x + 1");
    }

    [Test]
    public void Variable_Undefined()
    {
        ExpectError("$y = $undefined", "找不到");
    }

    [Test]
    public void Constant_DeclareAndUse()
    {
        ExpectBind("_PI = 3\n$v = _PI + 1");
    }

    [Test]
    public void Constant_Undefined()
    {
        ExpectError("$v = _UNDEFINED", "找不到");
    }

    [Test]
    public void Constant_CannotAugmentedAssign()
    {
        ExpectError("_X = 1\n_X += 2");
    }

    [Test]
    public void ExternVar_RequiresDeclaration()
    {
        // 未声明的外部变量应报错
        ExpectError("$v = @undeclared", "找不到");
    }

    [Test]
    public void ExternVar_DeclaredSucceeds()
    {
        var (success, _) = Compile("$v = @myvar", ImmutableHashSet.Create("myvar"));
        Assert.That(success, Is.True);
    }

    #endregion

    #region 函数作用域

    [Test]
    public void Function_DefineAndCall()
    {
        ExpectBind(@"
FUNC greet
    PRINT hello
ENDFUNC
CALL greet");
    }

    [Test]
    public void Function_Undefined()
    {
        ExpectError("CALL nonexistent", "找不到");
    }

    [Test]
    public void Function_ForwardReference()
    {
        // 函数可以在定义前调用（Phase 1 先收集声明）
        ExpectBind(@"
CALL foo
FUNC foo
    PRINT ok
ENDFUNC");
    }

    [Test]
    public void Function_CannotCallWithWrongArgCount()
    {
        ExpectError(@"
FUNC greet($a)
    PRINT hello & $a
ENDFUNC
CALL greet", "参数");
    }

    [Test]
    public void Function_LocalVariableInvisibleOutside()
    {
        // 函数内定义的变量对外不可见
        ExpectError(@"
FUNC setLocal
    $local = 10
ENDFUNC
CALL setLocal
$v = $local", "找不到");
    }

    [Test]
    public void Function_CanAccessGlobal()
    {
        ExpectBind(@"
$global = 100
FUNC readGlobal
    PRINT $global
ENDFUNC
CALL readGlobal");
    }

    #endregion

    #region 函数重载

    [Test]
    public void Overload_DifferentParamTypes()
    {
        ExpectBind(@"
FUNC fmt($x:int) : string
    RETURN $x
ENDFUNC
FUNC fmt($s:string) : string
    RETURN $s
ENDFUNC
fmt 42
fmt ""hello""");
    }

    [Test]
    public void Overload_DifferentParamCount()
    {
        ExpectBind(@"
FUNC bar($x:int)
    PRINT $x
ENDFUNC
FUNC bar($x:int, $y:int)
    $r = $x + $y
    PRINT $r
ENDFUNC
bar 1
bar 1, 2");
    }

    [Test]
    public void Overload_SameSignature_Error()
    {
        ExpectError(@"
FUNC foo($x:int)
    PRINT $x
ENDFUNC
FUNC foo($y:int)
    PRINT $y
ENDFUNC", "重复定义的函数");
    }

    [Test]
    public void Overload_BuiltinNameConflict_Error()
    {
        ExpectError(@"
FUNC WAIT($n:int)
    PRINT $n
ENDFUNC", "重复定义的函数");
    }

    #endregion

    #region 泛型推断

    [Test]
    public void Generic_LEN_String()
    {
        ExpectBind("$r = LEN(\"hello\")");
    }

    [Test]
    public void Generic_LEN_Array()
    {
        ExpectBind("$r = LEN([1, 2, 3])");
    }

    [Test]
    public void Generic_APPEND_Array()
    {
        ExpectBind("$a = [1, 2]\n$b = APPEND($a, 3)");
    }

    [Test]
    public void Generic_APPEND_TypeMismatch_Error()
    {
        // 向 int 数组追加 string → 类型约束冲突
        ExpectError("$a = [1, 2]\n$b = APPEND($a, \"wrong\")", "类型");
    }

    #endregion

    #region 类型检查 — 算术运算

    [Test]
    public void TypeCheck_IntArithmetic()
    {
        ExpectBind("$r = 1 + 2");
        ExpectBind("$r = 3 - 4");
        ExpectBind("$r = 5 * 6");
        ExpectBind("$r = 8 / 2");
        ExpectBind("$r = 10 % 3");
    }

    [Test]
    public void TypeCheck_DoubleArithmetic()
    {
        ExpectBind("$r = 1.5 + 2.5");
        ExpectBind("$r = 5.5 - 2.0");
        ExpectBind("$r = 2.5 * 4.0");
        ExpectBind("$r = 7.0 / 2.0");
    }

    [Test]
    public void TypeCheck_StringConcat_Plus()
    {
        ExpectBind("$r = \"hello\" + \" world\"");
    }

    [Test]
    public void TypeCheck_MixedTypes_Error()
    {
        // int 和 string 不支持 * 混合运算（只有 + 和 & 支持字符串隐式转换）
        ExpectError("$r = 1 * \"a\"", "不支持");
    }

    #endregion

    #region 类型检查 — 比较运算

    [Test]
    public void TypeCheck_IntComparison()
    {
        ExpectBind("$r = 1 == 2");
        ExpectBind("$r = 1 != 2");
        ExpectBind("$r = 1 < 2");
        ExpectBind("$r = 1 > 2");
        ExpectBind("$r = 1 <= 2");
        ExpectBind("$r = 1 >= 2");
    }

    [Test]
    public void TypeCheck_StringComparison()
    {
        ExpectBind("$r = \"abc\" == \"def\"");
        ExpectBind("$r = \"abc\" != \"def\"");
    }

    [Test]
    public void TypeCheck_DoubleComparison()
    {
        ExpectBind("$r = 1.5 < 2.5");
        ExpectBind("$r = 1.5 > 0.5");
        ExpectBind("$r = 1.5 == 1.5");
        ExpectBind("$r = 1.5 != 2.5");
        ExpectBind("$r = 1.5 <= 1.5");
        ExpectBind("$r = 1.5 >= 0.5");
    }

    #endregion

    #region 类型检查 — 逻辑运算

    [Test]
    public void TypeCheck_LogicalAndOr()
    {
        ExpectBind("$r = (1 == 1) and (2 == 2)");
        ExpectBind("$r = (1 == 1) or (2 == 0)");
    }

    [Test]
    public void TypeCheck_LogicalNot()
    {
        ExpectBind("$r = not (1 == 0)");
    }

    #endregion

    #region 类型检查 — 位运算

    [Test]
    public void TypeCheck_Bitwise()
    {
        ExpectBind("$r = 12 & 10");
        ExpectBind("$r = 12 | 10");
        ExpectBind("$r = 12 ^ 10");
        ExpectBind("$r = ~5");
        ExpectBind("$r = 1 << 4");
        ExpectBind("$r = 16 >> 2");
    }

    [Test]
    public void TypeCheck_BitwiseOnString_IsConcat()
    {
        // & 对字符串是拼接操作，不是位运算
        ExpectBind("$r = \"a\" & \"b\"");
    }

    #endregion

    #region 类型检查 — 控制流条件

    [Test]
    public void TypeCheck_IfCondition_MustBeBool()
    {
        // IF 条件必须是 bool 表达式
        ExpectBind("IF 1 == 1\nA\nENDIF");
        ExpectError("IF 1\nA\nENDIF", "类型不匹配");
    }

    [Test]
    public void TypeCheck_WhileCondition()
    {
        ExpectBind("$i = 0\nWHILE $i < 10\n$i += 1\nEND");
    }

    [Test]
    public void TypeCheck_UntilCondition()
    {
        ExpectBind("$i = 0\nUNTIL $i >= 10\n$i += 1\nEND");
    }

    #endregion

    #region 类型检查 — 复合赋值

    [Test]
    public void TypeCheck_AugmentedAssign_Int()
    {
        ExpectBind("$v = 0\n$v += 1\n$v -= 2\n$v *= 3\n$v /= 4\n$v %= 5");
    }

    [Test]
    public void TypeCheck_AugmentedAssign_Bitwise()
    {
        ExpectBind("$v = 0\n$v &= 1\n$v |= 2\n$v ^= 3\n$v <<= 4\n$v >>= 5");
    }

    [Test]
    public void TypeCheck_AugmentedAssign_String()
    {
        ExpectBind("$v = \"hello\"\n$v += \" world\"");
    }

    #endregion

    #region 内置函数签名

    [Test]
    public void Builtin_WAIT()
    {
        ExpectBind("WAIT 100");
    }

    [Test]
    public void Builtin_RAND()
    {
        ExpectBind("$r = RAND(100)");
    }

    [Test]
    public void Builtin_TIME()
    {
        ExpectBind("$t = TIME()");
    }

    [Test]
    public void Builtin_BEEP()
    {
        ExpectBind("BEEP 440, 1000");
    }

    [Test]
    public void Builtin_PRINT()
    {
        ExpectBind("PRINT hello");
    }

    [Test]
    public void Builtin_ALERT()
    {
        ExpectBind("ALERT warning");
    }

    #endregion

    #region 库脚本限制

    [Test]
    public void Lib_AllowsDefinitions()
    {
        // 库脚本允许变量、常量、函数定义
        var tree = SyntaxTree.Parse("$x = 10\n_CONST = 5\nFUNC f\nA\nENDFUNC", isLib: true);
        Assert.That(tree.Diagnostics.HasErrors(), Is.False);
    }

    [Test]
    public void Lib_ForbidsControlFlow()
    {
        var tree = SyntaxTree.Parse("IF $x == 1\nA\nENDIF", isLib: true);
        Assert.That(tree.Diagnostics.HasErrors(), Is.True);
    }

    [Test]
    public void Lib_ForbidsWait()
    {
        var tree = SyntaxTree.Parse("WAIT 100", isLib: true);
        Assert.That(tree.Diagnostics.HasErrors(), Is.True);
    }

    [Test]
    public void Lib_ForbidsKeyPress()
    {
        var tree = SyntaxTree.Parse("A", isLib: true);
        Assert.That(tree.Diagnostics.HasErrors(), Is.True);
    }

    #endregion

    #region 返回路径检查

    [Test]
    public void ReturnPath_AllPathsReturn()
    {
        ExpectBind(@"
FUNC max($a:int, $b:int) : int
    IF $a > $b
        RETURN $a
    ELSE
        RETURN $b
    ENDIF
ENDFUNC");
    }

    [Test]
    public void ReturnPath_MissingReturn_Allowed()
    {
        // 语言不强制所有路径都有返回值
        ExpectBind(@"
FUNC bad($x:int) : int
    IF $x > 0
        RETURN $x
    ENDIF
ENDFUNC");
    }

    #endregion

    #region 循环控制语义

    [Test]
    public void Break_OutsideLoop_Error()
    {
        ExpectError("BREAK", "跳出语句");
    }

    [Test]
    public void Continue_OutsideLoop_Error()
    {
        ExpectError("CONTINUE", "跳出语句");
    }

    [Test]
    public void Break_LevelExceedsNesting_Error()
    {
        ExpectError("FOR 3\nBREAK 5\nNEXT", "跳出语句");
    }

    #endregion
}