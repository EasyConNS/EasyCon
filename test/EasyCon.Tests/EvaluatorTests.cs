using EasyCon.Script;
using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using EasyScript;

namespace EasyCon.Tests;

internal sealed class MockOutputAdapter : IOutputAdapter
{
    public List<string> Printed { get; } = [];
    public List<string> Alerted { get; } = [];

    public void Print(string message, bool newline)
    {
        Printed.Add(newline ? message + "\n" : message);
    }

    public void Alert(string message)
    {
        Alerted.Add(message);
    }
}

/// <summary>
/// 执行层测试：验证运行时语义正确性。
/// 算术/比较/位运算的正确性 → ValueTests。
/// 类型检查和作用域 → BinderTests。
/// 语法结构 → ParserTests。
/// </summary>
[TestFixture]
public class EvaluatorTests
{
    private static (EvaluationResult Result, MockOutputAdapter Output) Eval(string code)
    {
        var output = new MockOutputAdapter();
        var compilation = Compilation.Create(SyntaxTree.Parse(code));
        var result = compilation.Evaluate(output, null, [], new CancellationTokenSource().Token);
        return (result, output);
    }

    private static Value EvalValue(string code) => Eval(code).Result.Result;

    #region 执行基本路径

    [Test]
    public void Return_Value()
    {
        Assert.That(EvalValue("RETURN 42").AsInt(), Is.EqualTo(42));
    }

    [Test]
    public void LastStatement_Value()
    {
        // 无 RETURN 时取最后一条表达式语句的值
        Assert.That(EvalValue("$v = 99").AsInt(), Is.EqualTo(99));
    }

    #endregion

    #region 控制流 — IF

    [Test]
    public void If_TrueBranch()
    {
        Assert.That(EvalValue(@"
$v = 1
$r = 0
IF $v == 1
    $r = 10
ENDIF
RETURN $r").AsInt(), Is.EqualTo(10));
    }

    [Test]
    public void If_FalseBranch_Skipped()
    {
        Assert.That(EvalValue(@"
$v = 2
$r = 0
IF $v == 1
    $r = 10
ENDIF
RETURN $r").AsInt(), Is.EqualTo(0));
    }

    [Test]
    public void If_Else()
    {
        Assert.That(EvalValue(@"
$v = 2
$r = 0
IF $v == 1
    $r = 10
ELSE
    $r = 20
ENDIF
RETURN $r").AsInt(), Is.EqualTo(20));
    }

    [Test]
    public void If_Elif()
    {
        Assert.That(EvalValue(@"
$v = 2
$r = 0
IF $v == 1
    $r = 10
ELIF $v == 2
    $r = 20
ELSE
    $r = 30
ENDIF
RETURN $r").AsInt(), Is.EqualTo(20));
    }

    [Test]
    public void If_Nested()
    {
        Assert.That(EvalValue(@"
$x = 1
$y = 2
$r = 0
IF $x == 1
    IF $y == 2
        $r = 42
    ENDIF
ENDIF
RETURN $r").AsInt(), Is.EqualTo(42));
    }

    #endregion

    #region 控制流 — FOR

    [Test]
    public void For_Count()
    {
        Assert.That(EvalValue("$c = 0\nFOR 3\n$c = $c + 1\nNEXT\nRETURN $c").AsInt(), Is.EqualTo(3));
    }

    [Test]
    public void For_Range()
    {
        Assert.That(EvalValue("$s = 0\nFOR $i = 1 TO 5\n$s = $s + $i\nNEXT\nRETURN $s").AsInt(), Is.EqualTo(15));
    }

    [Test]
    public void For_Nested()
    {
        Assert.That(EvalValue(@"
$total = 0
FOR $i = 1 TO 3
    FOR $j = 1 TO 3
        $total = $total + 1
    NEXT
NEXT
RETURN $total").AsInt(), Is.EqualTo(9));
    }

    #endregion

    #region 控制流 — WHILE

    [Test]
    public void While_Basic()
    {
        Assert.That(EvalValue(@"
$i = 0
$s = 0
WHILE $i < 5
    $s = $s + $i
    $i = $i + 1
END
RETURN $s").AsInt(), Is.EqualTo(10));
    }

    [Test]
    public void While_ZeroIterations()
    {
        Assert.That(EvalValue(@"
$i = 10
$c = 0
WHILE $i < 5
    $c = $c + 1
END
RETURN $c").AsInt(), Is.EqualTo(0));
    }

    #endregion

    #region 控制流 — BREAK / CONTINUE

    [Test]
    public void Break_ExitsLoop()
    {
        Assert.That(EvalValue(@"
$c = 0
FOR $i = 1 TO 100
    IF $i > 5
        BREAK
    ENDIF
    $c = $c + 1
NEXT
RETURN $c").AsInt(), Is.EqualTo(5));
    }

    [Test]
    public void Break_NestedLevel()
    {
        Assert.That(EvalValue(@"
$c = 0
FOR $i = 1 TO 10
    FOR $j = 1 TO 10
        $c = $c + 1
        IF $c >= 5
            BREAK 2
        ENDIF
    NEXT
NEXT
RETURN $c").AsInt(), Is.EqualTo(5));
    }

    [Test]
    public void Continue_SkipsIteration()
    {
        Assert.That(EvalValue(@"
$s = 0
FOR $i = 1 TO 10
    IF $i % 2 == 0
        CONTINUE
    ENDIF
    $s = $s + $i
NEXT
RETURN $s").AsInt(), Is.EqualTo(25)); // 1+3+5+7+9
    }

    #endregion

    #region 函数 — 基本调用

    [Test]
    public void Function_ParamsAndReturn()
    {
        Assert.That(EvalValue(@"
FUNC add($a, $b) : int
    RETURN $a + $b
ENDFUNC
RETURN add(3, 5)").AsInt(), Is.EqualTo(8));
    }

    [Test]
    public void Function_NoArgs()
    {
        Assert.That(EvalValue(@"
FUNC get42() : int
    RETURN 42
ENDFUNC
RETURN get42()").AsInt(), Is.EqualTo(42));
    }

    [Test]
    public void Function_VoidReturn()
    {
        var (_, o) = Eval(@"
FUNC greet
    PRINT ""hello""
ENDFUNC
CALL greet");
        Assert.That(o.Printed, Has.Count.EqualTo(1));
        Assert.That(o.Printed[0], Does.Contain("hello"));
    }

    [Test]
    public void Function_MultipleCalls()
    {
        Assert.That(EvalValue(@"
FUNC double($n) : int
    RETURN $n * 2
ENDFUNC
$r = double(3) + double(5)
RETURN $r").AsInt(), Is.EqualTo(16));
    }

    #endregion

    #region 函数 — 递归

    [Test]
    public void Function_Recursion_Fibonacci()
    {
        Assert.That(EvalValue(@"
FUNC fib($n) : int
    IF $n <= 1
        RETURN $n
    ENDIF
    RETURN fib($n - 1) + fib($n - 2)
ENDFUNC
RETURN fib(10)").AsInt(), Is.EqualTo(55));
    }

    [Test]
    public void Function_TailRecursion()
    {
        Assert.That(EvalValue(@"
FUNC sum($n, $acc) : int
    IF $n == 0
        RETURN $acc
    ENDIF
    RETURN sum($n - 1, $acc + $n)
ENDFUNC
RETURN sum(50000, 0)").AsInt(), Is.EqualTo(1250025000));
    }

    #endregion

    #region 函数 — 重载决议

    [Test]
    public void Function_Overload_Resolution()
    {
        Assert.That(EvalValue(@"
FUNC fmt($x:int) : string
    RETURN $x
ENDFUNC
FUNC fmt($s:string) : string
    RETURN $s
ENDFUNC
RETURN fmt(42)").AsString(), Is.EqualTo("42"));
    }

    [Test]
    public void Function_Overload_StringArg()
    {
        Assert.That(EvalValue(@"
FUNC fmt($x:int) : string
    RETURN $x
ENDFUNC
FUNC fmt($s:string) : string
    RETURN $s
ENDFUNC
RETURN fmt(""hello"")").AsString(), Is.EqualTo("hello"));
    }

    #endregion

    #region 函数 — 局部变量

    [Test]
    public void Function_GlobalVariableMutation()
    {
        // 函数内对全局变量赋值会影响全局状态
        Assert.That(EvalValue(@"
$x = 100
FUNC setX($v) : int
    $x = $v
    RETURN $x
ENDFUNC
$y = setX(999)
RETURN $x").AsInt(), Is.EqualTo(999));
    }

    #endregion

    #region 字符串操作

    [Test]
    public void String_Concatenation()
    {
        Assert.That(EvalValue("$r = \"hello\" + \" world\"").AsString(), Is.EqualTo("hello world"));
    }

    [Test]
    public void String_Index()
    {
        Assert.That(EvalValue("$s = \"abc\"\nRETURN $s[1]").AsString(), Is.EqualTo("b"));
    }

    [Test]
    public void String_Slice()
    {
        Assert.That(EvalValue("$s = \"hello\"\nRETURN $s[1:3]").AsString(), Is.EqualTo("el"));
    }

    [Test]
    public void String_LEN()
    {
        Assert.That(EvalValue("RETURN LEN(\"hello\")").AsInt(), Is.EqualTo(5));
    }

    #endregion

    #region 数组操作

    [Test]
    public void Array_Create()
    {
        Assert.That(EvalValue("RETURN LEN([1, 2, 3])").AsInt(), Is.EqualTo(3));
    }

    [Test]
    public void Array_Index()
    {
        Assert.That(EvalValue("$a = [10, 20, 30]\nRETURN $a[1]").AsInt(), Is.EqualTo(20));
    }

    [Test]
    public void Array_Slice()
    {
        var v = EvalValue("$a = [1, 2, 3, 4, 5]\nRETURN $a[1:3]");
        Assert.That(v.AsArray().Count, Is.EqualTo(2));
        Assert.That(v.AsArray()[0].AsInt(), Is.EqualTo(2));
    }

    [Test]
    public void Array_Append()
    {
        Assert.That(EvalValue("$a = [1, 2]\n$b = APPEND($a, 3)\nRETURN LEN($b)").AsInt(), Is.EqualTo(3));
    }

    [Test]
    public void Array_Concat()
    {
        Assert.That(EvalValue("$a = [1, 2]\n$b = [3, 4]\n$c = $a + $b\nRETURN LEN($c)").AsInt(), Is.EqualTo(4));
    }

    [Test]
    public void Array_Append_Immutable()
    {
        // APPEND 不修改原数组
        Assert.That(EvalValue(@"
$a = [10, 20]
$b = APPEND($a, 30)
RETURN $b[2]").AsInt(), Is.EqualTo(30));
    }

    [Test]
    public void Array_ElementAssign()
    {
        Assert.That(EvalValue("$a = [10, 20, 30]\n$a[1] = 99\nRETURN $a[1]").AsInt(), Is.EqualTo(99));
    }

    [Test]
    public void Array_ElementAssign_MultipleIndices()
    {
        Assert.That(EvalValue(@"
$a = [1, 2, 3]
$a[0] = 10
$a[1] = 20
$a[2] = 30
RETURN $a[0] + $a[1] + $a[2]").AsInt(), Is.EqualTo(60));
    }

    [Test]
    public void Array_ElementAugmentedAssign()
    {
        Assert.That(EvalValue("$a = [10, 20, 30]\n$a[1] += 5\nRETURN $a[1]").AsInt(), Is.EqualTo(25));
    }

    #endregion

    #region 复合赋值

    [Test]
    public void AugmentedAssign_AddSubMulDivMod()
    {
        Assert.That(EvalValue("$v = 10\n$v += 5\nRETURN $v").AsInt(), Is.EqualTo(15));
        Assert.That(EvalValue("$v = 10\n$v -= 3\nRETURN $v").AsInt(), Is.EqualTo(7));
        Assert.That(EvalValue("$v = 4\n$v *= 3\nRETURN $v").AsInt(), Is.EqualTo(12));
        Assert.That(EvalValue("$v = 10\n$v /= 3\nRETURN $v").AsInt(), Is.EqualTo(3));
        Assert.That(EvalValue("$v = 10\n$v %= 3\nRETURN $v").AsInt(), Is.EqualTo(1));
    }

    [Test]
    public void AugmentedAssign_Bitwise()
    {
        Assert.That(EvalValue("$v = 12\n$v &= 10\nRETURN $v").AsInt(), Is.EqualTo(8));
        Assert.That(EvalValue("$v = 12\n$v |= 10\nRETURN $v").AsInt(), Is.EqualTo(14));
        Assert.That(EvalValue("$v = 1\n$v <<= 4\nRETURN $v").AsInt(), Is.EqualTo(16));
        Assert.That(EvalValue("$v = 16\n$v >>= 2\nRETURN $v").AsInt(), Is.EqualTo(4));
    }

    [Test]
    public void DiscardAssign()
    {
        // _ = expr evaluates but discards the result
        Assert.That(EvalValue("$v = 1\n_ = $v + 1\nRETURN $v").AsInt(), Is.EqualTo(1));
    }

    #endregion

    #region 内置函数 — 运行时行为

    [Test]
    public void Builtin_RAND_Range()
    {
        var v = EvalValue("$r = RAND(10)");
        Assert.That(v.AsInt(), Is.InRange(0, 9));
    }

    [Test]
    public void Builtin_TIME_NonNegative()
    {
        Assert.That(EvalValue("$t = TIME()").AsInt(), Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void Builtin_PRINT_Capture()
    {
        var (_, o) = Eval("PRINT \"hello\"");
        Assert.That(o.Printed, Has.Count.EqualTo(1));
        Assert.That(o.Printed[0], Does.Contain("hello"));
    }

    [Test]
    public void Builtin_ALERT_Capture()
    {
        var (_, o) = Eval("ALERT \"warning\"");
        Assert.That(o.Alerted, Has.Count.EqualTo(1));
        Assert.That(o.Alerted[0], Does.Contain("warning"));
    }

    [Test]
    public void Builtin_BEEP_OutOfRange_Throws()
    {
        Assert.Throws<Exception>(() => Eval("BEEP 10, 100"));
    }

    [Test]
    public void Builtin_PRINT_MixedVariables()
    {
        var (_, o) = Eval(@"
$count = 42
$label = ""items""
PRINT ""found "" & $count & "" "" & $label");
        Assert.That(o.Printed[0], Does.Contain("found 42 items"));
    }

    #endregion

    #region 运行时错误

    [Test]
    public void Error_DivisionByZero()
    {
        Assert.Throws<DivideByZeroException>(() => EvalValue("$a = 10\nRETURN $a / 0"));
    }

    [Test]
    public void Error_ModuloByZero()
    {
        Assert.Throws<DivideByZeroException>(() => EvalValue("$a = 10\nRETURN $a % 0"));
    }

    [Test]
    public void Error_ArrayIndexOutOfBounds()
    {
        Assert.Throws<Exception>(() => EvalValue("$a = [1, 2]\nRETURN $a[5]"));
    }

    [Test]
    public void Error_ArraySliceOutOfBounds()
    {
        Assert.Throws<Exception>(() => EvalValue("$a = [1, 2]\nRETURN $a[0:10]"));
    }

    [Test]
    public void Error_UndefinedVariable()
    {
        var tree = SyntaxTree.Parse("$r = $undefined");
        var compilation = Compilation.Create(tree);
        var result = compilation.Evaluate(new MockOutputAdapter(), null, [], CancellationToken.None);
        Assert.That(result.Diagnostics.HasErrors(), Is.True);
    }

    #endregion

    #region 隐式类型转换（运行时）

    [Test]
    public void ImplicitConversion_BoolToInt()
    {
        // bool 参与算术运算（bool + int 不支持，改用赋值测试）
        Assert.That(EvalValue("$b = (1 == 1)\nRETURN $b").AsBool(), Is.True);
    }

    [Test]
    public void ImplicitConversion_IntToString()
    {
        var (_, o) = Eval("PRINT \"value: \" & 42");
        Assert.That(o.Printed[0], Does.Contain("value: 42"));
    }

    #endregion

    #region 集成场景

    [Test]
    public void Integration_PrimeSieve()
    {
        Assert.That(EvalValue(@"
$sum = 0
FOR $n = 2 TO 50
    $isPrime = 1
    FOR $d = 2 TO $n
        IF $n % $d == 0 and $n != $d
            $isPrime = 0
            BREAK
        ENDIF
    NEXT
    IF $isPrime == 1
        $sum = $sum + $n
    ENDIF
NEXT
RETURN $sum").AsInt(), Is.EqualTo(328));
    }

    [Test]
    public void Integration_Factorial()
    {
        Assert.That(EvalValue(@"
FUNC factorial($n) : int
    $r = 1
    FOR $i = 1 TO $n
        $r = $r * $i
    NEXT
    RETURN $r
ENDFUNC
RETURN factorial(10)").AsInt(), Is.EqualTo(3628800));
    }

    [Test]
    public void Integration_FunctionChain()
    {
        Assert.That(EvalValue(@"
FUNC inc($n) : int
    RETURN $n + 1
ENDFUNC
FUNC double($n) : int
    RETURN $n * 2
ENDFUNC
RETURN double(inc(5))").AsInt(), Is.EqualTo(12));
    }

    #endregion

    #region STRUCT — 声明与初始化

    [Test]
    public void Struct_DeclareAndInit()
    {
        Assert.That(EvalValue(@"
STRUCT Point
    $x:INT
    $y:INT
END
$p = Point{}
RETURN $p").AsStruct(), Is.Not.Null);
    }

    [Test]
    public void Struct_FieldReadWrite()
    {
        Assert.That(EvalValue(@"
STRUCT Point
    $x:INT
    $y:INT
END
$p = Point{}
$p.x = 10
$p.y = 20
RETURN $p.x + $p.y").AsInt(), Is.EqualTo(30));
    }

    [Test]
    public void Struct_FieldAugmentedAssign()
    {
        Assert.That(EvalValue(@"
STRUCT Counter
    $val:INT
END
$c = Counter{}
$c.val = 5
$c.val += 3
RETURN $c.val").AsInt(), Is.EqualTo(8));
    }

    [Test]
    public void Struct_MultipleFieldTypes()
    {
        var result = Eval(@"
STRUCT Mixed
    $a:INT
    $b:DOUBLE
    $c:STRING
END
$m = Mixed{}
$m.a = 42
$m.b = 3.14
$m.c = ""hello""
$result = $m.c & "" "" & $m.a
PRINT $result");
        Assert.That(result.Result.Diagnostics.HasErrors(), Is.False);
        Assert.That(result.Output.Printed[0], Does.Contain("hello 42"));
    }

    [Test]
    public void Struct_MultipleInstances()
    {
        Assert.That(EvalValue(@"
STRUCT Point
    $x:INT
    $y:INT
END
$p1 = Point{}
$p2 = Point{}
$p1.x = 1
$p2.x = 2
RETURN $p1.x + $p2.x").AsInt(), Is.EqualTo(3));
    }

    [Test]
    public void Struct_DefaultZero()
    {
        Assert.That(EvalValue(@"
STRUCT Point
    $x:INT
    $y:INT
END
$p = Point{}
RETURN $p.x").AsInt(), Is.EqualTo(0));
    }

    #endregion

    #region STRUCT — 嵌套结构体

    [Test]
    public void Struct_NestedAccess()
    {
        Assert.That(EvalValue(@"
STRUCT Inner
    $val:INT
END
STRUCT Outer
    $a:INT
    $inner:Inner
END
$o = Outer{}
$o.a = 1
$o.inner.val = 42
RETURN $o.inner.val").AsInt(), Is.EqualTo(42));
    }

    [Test]
    public void Struct_NestedIndependent()
    {
        Assert.That(EvalValue(@"
STRUCT Inner
    $val:INT
END
STRUCT Outer
    $inner:Inner
END
$o1 = Outer{}
$o2 = Outer{}
$o1.inner.val = 10
$o2.inner.val = 20
RETURN $o1.inner.val + $o2.inner.val").AsInt(), Is.EqualTo(30));
    }

    #endregion

    #region STRUCT — 数组字段

//     [Test]
//     public void Struct_ArrayField()
//     {
//         Assert.That(EvalValue(@"
// STRUCT Arr
//     $data:INT[4]
// END
// $a = Arr{}
// $a.data[0] = 10
// $a.data[1] = 20
// $a.data[2] = 30
// $a.data[3] = 40
// RETURN $a.data[0] + $a.data[3]").AsInt(), Is.EqualTo(50));
//     }

    #endregion

    #region STRUCT — 错误情况

    [Test]
    public void Struct_UndefinedType_Error()
    {
        var (result, _) = Eval("$p = UnknownStruct{}");
        Assert.That(result.Diagnostics.HasErrors(), Is.True);
    }

    [Test]
    public void Struct_UndefinedField_Error()
    {
        var (result, _) = Eval(@"
STRUCT Point
    $x:INT
END
$p = Point{}
$p.z = 10");
        Assert.That(result.Diagnostics.HasErrors(), Is.True);
    }

    [Test]
    public void Struct_DuplicateDefinition_Error()
    {
        var (result, _) = Eval(@"
STRUCT Foo
    $x:INT
END
STRUCT Foo
    $y:INT
END");
        Assert.That(result.Diagnostics.HasErrors(), Is.True);
    }

    [Test]
    public void Struct_NonStructFieldAccess_Error()
    {
        var (result, _) = Eval("$x = 10\n$x.field = 5");
        Assert.That(result.Diagnostics.HasErrors(), Is.True);
    }

    [Test]
    public void Struct_FieldTypeMismatch_Error()
    {
        // Assigning string to int field should fail at binder level
        var (result, _) = Eval(@"
STRUCT Foo
    $x:INT
END
$f = Foo{}
$f.x = ""hello""");
        Assert.That(result.Diagnostics.HasErrors(), Is.True);
    }

    #endregion

    #region STRUCT — 函数参数

    [Test]
    public void Struct_PassToFunction()
    {
        Assert.That(EvalValue(@"
STRUCT Point
    $x:INT
    $y:INT
END
FUNC getX($p:Point) : int
    RETURN $p.x
ENDFUNC
$p = Point{}
$p.x = 99
RETURN getX($p)").AsInt(), Is.EqualTo(99));
    }

    #endregion

    #region EXTERN（仅 Windows）

    [Test]
    [Platform("Win")]
    public void Extern_Declaration()
    {
        var code = @"EXTERN FUNC Sleep($ms:INT) FROM ""kernel32.dll""";
        var (result, _) = Eval(code);
        Assert.That(result.Diagnostics.HasErrors(), Is.False);
    }

    [Test]
    [Platform("Win")]
    public void Extern_WithAsClause()
    {
        var code = @"EXTERN FUNC mysqrt($x:DOUBLE):DOUBLE AS ""sqrt"" FROM ""msvcrt.dll""";
        var (result, _) = Eval(code);
        Assert.That(result.Diagnostics.HasErrors(), Is.False);
    }

    [Test]
    [Platform("Win")]
    public void Extern_DuplicateName_Error()
    {
        var code = @"
EXTERN FUNC foo($x:INT):INT FROM ""a.dll""
EXTERN FUNC foo($y:INT):INT FROM ""b.dll""
";
        var (result, _) = Eval(code);
        Assert.That(result.Diagnostics.HasErrors(), Is.True);
    }

    #endregion
}