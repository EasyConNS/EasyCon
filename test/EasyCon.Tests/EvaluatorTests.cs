using EasyCon.Script;
using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using EasyScript;
using System.Collections.Immutable;

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
    private static (EvaluationResult Result, MockOutputAdapter Output) Eval(
        string code,
        ImmutableArray<ForeignFunction> ffi = default)
    {
        var output = new MockOutputAdapter();
        var compilation = Compilation.Create(SyntaxTree.Parse(code));
        var result = compilation.Evaluate(output, null, [], new CancellationTokenSource().Token, ffi);
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

    #region FFI 函数

    [Test]
    public void FFI_SimpleCall()
    {
        var ffi = new ForeignFunction
        {
            Name = "DOUBLE",
            Parameters = [("x", ScriptType.Int)],
            ReturnType = ScriptType.Int,
            Invoke = args => args[0].AsInt() * 2,
        };
        var (result, _) = Eval("$r = DOUBLE(7)", [ffi]);
        Assert.That(result.Result.AsInt(), Is.EqualTo(14));
    }

    [Test]
    public void FFI_MultipleArgs()
    {
        var ffi = new ForeignFunction
        {
            Name = "MUL",
            Parameters = [("a", ScriptType.Int), ("b", ScriptType.Int)],
            ReturnType = ScriptType.Int,
            Invoke = args => args[0].AsInt() * args[1].AsInt(),
        };
        var (result, _) = Eval("$r = MUL(6, 7)", [ffi]);
        Assert.That(result.Result.AsInt(), Is.EqualTo(42));
    }

    [Test]
    public void FFI_StatementCall()
    {
        var ffi = new ForeignFunction
        {
            Name = "LOG",
            Parameters = [("msg", ScriptType.String)],
            ReturnType = ScriptType.Void,
            Invoke = args => Value.Void,
        };
        var (result, _) = Eval("LOG \"test\"", [ffi]);
        Assert.That(result.Diagnostics.HasErrors(), Is.False);
    }

    [Test]
    public void FFI_MixedWithUserFunc()
    {
        var ffi = new ForeignFunction
        {
            Name = "DOUBLE",
            Parameters = [("x", ScriptType.Int)],
            ReturnType = ScriptType.Int,
            Invoke = args => args[0].AsInt() * 2,
        };
        var (result, _) = Eval(@"
FUNC add($a, $b) : int
    RETURN $a + $b
ENDFUNC
$r = DOUBLE(add(3, 4))", [ffi]);
        Assert.That(result.Result.AsInt(), Is.EqualTo(14));
    }

    [Test]
    public void FFI_AndBuiltinTogether()
    {
        var ffi = new ForeignFunction
        {
            Name = "CLAMP",
            Parameters = [("val", ScriptType.Int), ("max", ScriptType.Int)],
            ReturnType = ScriptType.Int,
            Invoke = args => Math.Min(args[0].AsInt(), args[1].AsInt()),
        };
        // RAND(100) + CLAMP 应该正常工作
        var (result, _) = Eval("$rand_val = RAND(100)\n$r = CLAMP($rand_val, 50)", [ffi]);
        Assert.That(result.Result.AsInt(), Is.InRange(0, 50));
    }

    #endregion
}