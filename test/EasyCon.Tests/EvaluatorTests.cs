using EasyCon.Script;
using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using EasyScript;

namespace EasyCon.Tests;

/// <summary>
/// 模拟输出适配器，捕获 PRINT 和 ALERT 输出
/// </summary>
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

    #region 内置函数

    [Test]
    public void Builtin_Rand_ReturnsInt()
    {
        var (result, _) = Eval("$r = RAND(10)");
        Assert.That(result.Result.AsInt(), Is.InRange(0, 9));
    }

    [Test]
    public void Builtin_Time_ReturnsNonNegative()
    {
        var (result, _) = Eval("$t = TIME()");
        Assert.That(result.Result.AsInt(), Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void Builtin_Print_CapturesOutput()
    {
        var (_, output) = Eval("PRINT \"hello\"");
        Assert.That(output.Printed, Has.Count.EqualTo(1));
        Assert.That(output.Printed[0], Does.Contain("hello"));
    }

    [Test]
    public void Builtin_Len_Array()
    {
        var (result, _) = Eval("$arr = [1, 2, 3]\n$r = LEN($arr)");
        Assert.That(result.Result.AsInt(), Is.EqualTo(3));
    }

    [Test]
    public void Builtin_Len_String()
    {
        var (result, _) = Eval("$r = LEN(\"abc\")");
        Assert.That(result.Result.AsInt(), Is.EqualTo(3));
    }

    [Test]
    public void Builtin_Append_Array()
    {
        var (result, _) = Eval("$arr = [1, 2]\n$appended = APPEND($arr, 3)\n$r = LEN($appended)");
        Assert.That(result.Result.AsInt(), Is.EqualTo(3));
    }

    #endregion

    #region 用户函数

    [Test]
    public void UserFunc_SimpleAdd()
    {
        var (result, _) = Eval(@"
FUNC add($a, $b) : int
    RETURN $a + $b
ENDFUNC
$r = add(3, 5)");
        Assert.That(result.Result.AsInt(), Is.EqualTo(8));
    }

    [Test]
    public void UserFunc_CalledTwice()
    {
        var (result, _) = Eval(@"
FUNC add($a, $b) : int
    RETURN $a + $b
ENDFUNC
$x = add(1, 2)
$r = add($x, 10)");
        Assert.That(result.Result.AsInt(), Is.EqualTo(13));
    }

    [Test]
    public void UserFunc_Recursive()
    {
        var (result, _) = Eval(@"
FUNC fib($n) : int
    IF $n <= 1
        RETURN $n
    ENDIF
    RETURN fib($n - 1) + fib($n - 2)
ENDFUNC
$r = fib(10)");
        Assert.That(result.Result.AsInt(), Is.EqualTo(55));
    }

    [Test]
    public void UserFunc_VoidReturn()
    {
        var (_, output) = Eval(@"
FUNC greet
    PRINT ""hello""
ENDFUNC
CALL greet");
        Assert.That(output.Printed, Has.Count.EqualTo(1));
        Assert.That(output.Printed[0], Does.Contain("hello"));
    }

    #endregion

    #region EXTERN 解析测试

    [Test]
    public void Extern_Parse_SimpleDecl()
    {
        var code = @"EXTERN FUNC Sleep($ms:INT):VOID FROM ""kernel32.dll""";
        var (result, _) = Eval(code);
        Assert.That(result.Diagnostics.HasErrors(), Is.False);
    }

    [Test]
    public void Extern_Parse_MultipleParams()
    {
        var code = @"EXTERN FUNC MessageBoxW($hwnd:PTR, $text:STRING, $caption:STRING, $flags:INT):INT FROM ""user32.dll""";
        var (result, _) = Eval(code);
        Assert.That(result.Diagnostics.HasErrors(), Is.False);
    }

    [Test]
    public void Extern_Parse_NoParams()
    {
        var code = @"EXTERN FUNC GetForegroundWindow():PTR FROM ""user32.dll""";
        var (result, _) = Eval(code);
        Assert.That(result.Diagnostics.HasErrors(), Is.False);
    }

    [Test]
    public void Extern_Parse_DoubleType()
    {
        var code = @"EXTERN FUNC sqrt($x:DOUBLE):DOUBLE FROM ""msvcrt.dll""";
        var (result, _) = Eval(code);
        Assert.That(result.Diagnostics.HasErrors(), Is.False);
    }

    [Test]
    public void Extern_Parse_DuplicateName_Error()
    {
        var code = @"
EXTERN FUNC foo($x:INT):INT FROM ""a.dll""
EXTERN FUNC foo($y:INT):INT FROM ""b.dll""
";
        var (result, _) = Eval(code);
        Assert.That(result.Diagnostics.HasErrors(), Is.True);
    }

    #endregion

    #region 类型间操作测试

    #region INT 与 INT

    [Test]
    public void Op_IntAdd() => Assert.That(Eval("$r = 3 + 5").Result.Result.AsInt(), Is.EqualTo(8));

    [Test]
    public void Op_IntSub() => Assert.That(Eval("$r = 10 - 4").Result.Result.AsInt(), Is.EqualTo(6));

    [Test]
    public void Op_IntMul() => Assert.That(Eval("$r = 3 * 7").Result.Result.AsInt(), Is.EqualTo(21));

    [Test]
    public void Op_IntDiv() => Assert.That(Eval("$r = 17 / 5").Result.Result.AsInt(), Is.EqualTo(3));

    [Test]
    public void Op_IntMod() => Assert.That(Eval("$r = 17 % 5").Result.Result.AsInt(), Is.EqualTo(2));

    [Test]
    public void Op_IntEql() => Assert.That(Eval("$r = 3 == 3").Result.Result.AsBool(), Is.True);

    [Test]
    public void Op_IntNeq() => Assert.That(Eval("$r = 3 != 4").Result.Result.AsBool(), Is.True);

    [Test]
    public void Op_IntLess() => Assert.That(Eval("$r = 2 < 5").Result.Result.AsBool(), Is.True);

    [Test]
    public void Op_IntGreater() => Assert.That(Eval("$r = 7 > 3").Result.Result.AsBool(), Is.True);

    [Test]
    public void Op_IntLeq() => Assert.That(Eval("$r = 5 <= 5").Result.Result.AsBool(), Is.True);

    [Test]
    public void Op_IntGeq() => Assert.That(Eval("$r = 5 >= 4").Result.Result.AsBool(), Is.True);

    [Test]
    public void Op_IntBitAnd() => Assert.That(Eval("$r = 12 & 10").Result.Result.AsInt(), Is.EqualTo(8));

    [Test]
    public void Op_IntBitOr() => Assert.That(Eval("$r = 12 | 10").Result.Result.AsInt(), Is.EqualTo(14));

    [Test]
    public void Op_IntBitXor() => Assert.That(Eval("$r = 12 ^ 10").Result.Result.AsInt(), Is.EqualTo(6));

    [Test]
    public void Op_IntShl() => Assert.That(Eval("$r = 1 << 4").Result.Result.AsInt(), Is.EqualTo(16));

    [Test]
    public void Op_IntShr() => Assert.That(Eval("$r = 16 >> 2").Result.Result.AsInt(), Is.EqualTo(4));

    #endregion

    #region DOUBLE 与 DOUBLE

    [Test]
    public void Op_DoubleAdd() => Assert.That(Eval("$r = 1.5 + 2.5").Result.Result.AsDouble(), Is.EqualTo(4.0));

    [Test]
    public void Op_DoubleSub() => Assert.That(Eval("$r = 5.5 - 2.0").Result.Result.AsDouble(), Is.EqualTo(3.5));

    [Test]
    public void Op_DoubleMul() => Assert.That(Eval("$r = 2.5 * 4.0").Result.Result.AsDouble(), Is.EqualTo(10.0));

    [Test]
    public void Op_DoubleDiv() => Assert.That(Eval("$r = 7.0 / 2.0").Result.Result.AsDouble(), Is.EqualTo(3.5));

    [Test]
    public void Op_DoubleViaVariable()
    {
        var (result, _) = Eval(@"$a = 3.0
$b = 4.0
$r = $a * $a + $b * $b");
        Assert.That(result.Result.AsDouble(), Is.EqualTo(25.0));
    }

    #endregion

    #region INT 与 DOUBLE 混合 (通过 Value operator+)

    [Test]
    public void Op_IntPlusDouble()
    {
        var a = Value.FromInt(3);
        var b = Value.FromDouble(1.5);
        var result = a + b;
        Assert.That(result.Type, Is.EqualTo(ScriptType.Double));
        Assert.That(result.AsDouble(), Is.EqualTo(4.5));
    }

    [Test]
    public void Op_DoublePlusInt()
    {
        var a = Value.FromDouble(2.5);
        var b = Value.FromInt(3);
        var result = a + b;
        Assert.That(result.Type, Is.EqualTo(ScriptType.Double));
        Assert.That(result.AsDouble(), Is.EqualTo(5.5));
    }

    #endregion

    #region BOOL 操作

    [Test]
    public void Op_BoolEql() => Assert.That(Eval("$r = (1 == 1) == (1 == 1)").Result.Result.AsBool(), Is.True);

    [Test]
    public void Op_BoolNeq() => Assert.That(Eval("$r = (1 == 1) != (1 == 0)").Result.Result.AsBool(), Is.True);

    [Test]
    public void Op_BoolAnd() => Assert.That(Eval("$r = (1 == 1) and (1 == 0)").Result.Result.AsBool(), Is.False);

    [Test]
    public void Op_BoolOr() => Assert.That(Eval("$r = (1 == 1) or (1 == 0)").Result.Result.AsBool(), Is.True);

    [Test]
    public void Op_BoolShortCircuitAnd()
    {
        Assert.That(Eval("$r = (1 == 0) and (1 == 1)").Result.Result.AsBool(), Is.False);
    }

    [Test]
    public void Op_BoolShortCircuitOr()
    {
        Assert.That(Eval("$r = (1 == 1) or (1 == 0)").Result.Result.AsBool(), Is.True);
    }

    #endregion

    #region STRING 操作

    [Test]
    public void Op_StringConcat() => Assert.That(Eval("$r = \"hello\" + \" world\"").Result.Result.AsString(), Is.EqualTo("hello world"));

    [Test]
    public void Op_StringEql() => Assert.That(Eval("$r = \"abc\" == \"abc\"").Result.Result.AsBool(), Is.True);

    [Test]
    public void Op_StringNeq() => Assert.That(Eval("$r = \"abc\" != \"def\"").Result.Result.AsBool(), Is.True);

    #endregion

    #region PTR 操作

    [Test]
    public void Op_PtrEqual()
    {
        var a = Value.FromPtr(0);
        var b = Value.FromPtr(0);
        Assert.That(a == b, Is.True);
    }

    [Test]
    public void Op_PtrNotEqual()
    {
        var a = Value.FromPtr(0);
        var b = Value.FromPtr(42);
        Assert.That(a != b, Is.True);
    }

    [Test]
    public void Op_PtrNullIsZero()
    {
        var p = Value.FromPtr(0);
        Assert.That(p.AsPtr(), Is.EqualTo(0));
    }

    [Test]
    public void Op_PtrFromLong()
    {
        var p = Value.FromPtr(0xDEADBEEF);
        Assert.That(p.AsPtr(), Is.EqualTo(0xDEADBEEF));
    }

    #endregion

    #region 类型转换

    [Test]
    public void Op_IntToBool_True() => Assert.That(Eval("$r = not (1 == 0)").Result.Result.AsBool(), Is.True);

    [Test]
    public void Op_IntToBool_False() => Assert.That(Eval("$r = not (1 == 1)").Result.Result.AsBool(), Is.False);

    [Test]
    public void Op_DoubleToBool_NonZero()
    {
        var v = Value.FromDouble(3.14);
        Assert.That(v.ToBoolean(), Is.True);
    }

    [Test]
    public void Op_DoubleToBool_Zero()
    {
        var v = Value.FromDouble(0.0);
        Assert.That(v.ToBoolean(), Is.False);
    }

    [Test]
    public void Op_PtrToBool_NonZero()
    {
        var v = Value.FromPtr(42);
        Assert.That(v.ToBoolean(), Is.True);
    }

    [Test]
    public void Op_PtrToBool_Zero()
    {
        var v = Value.FromPtr(0);
        Assert.That(v.ToBoolean(), Is.False);
    }

    [Test]
    public void Op_ImplicitDoubleFromDouble()
    {
        Value v = 3.14;
        Assert.That(v.Type, Is.EqualTo(ScriptType.Double));
        Assert.That(v.AsDouble(), Is.EqualTo(3.14));
    }

    [Test]
    public void Op_ImplicitPtrFromLong()
    {
        Value v = 123L;
        Assert.That(v.Type, Is.EqualTo(ScriptType.Ptr));
        Assert.That(v.AsPtr(), Is.EqualTo(123));
    }

    #endregion

    #endregion
}