using EasyCon.Script;
using EasyCon.Script.Syntax;
using EasyScript;
using System.Collections.Immutable;

namespace EasyCon.Tests;

/// <summary>
/// 测试库脚本自动加载、隔离绑定、解析限制
/// </summary>
[TestFixture]
public class LibTests
{
    private string _tempDir = null!;
    private string _libDir = null!;

    [SetUp]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"EasyConTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _libDir = Path.Combine(_tempDir, "lib");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private string WriteMain(string code)
    {
        var path = Path.Combine(_tempDir, "main.ecs");
        File.WriteAllText(path, code);
        return path;
    }

    private string WriteLib(string fileName, string code)
    {
        Directory.CreateDirectory(_libDir);
        var path = Path.Combine(_libDir, fileName);
        File.WriteAllText(path, code);
        return path;
    }

    private static (Compilation Compilation, bool Success, List<string> Errors) CompileFile(string filePath)
    {
        var tree = SyntaxTree.Load(filePath);
        var errors = tree.Diagnostics.Where(d => d.IsError).Select(d => d.Message).ToList();
        if (errors.Count == 0)
        {
            try
            {
                var compilation = Compilation.Create(tree);
                var diag = compilation.Compile([]);
                foreach (var d in diag)
                    errors.Add(d.Message);
                if (errors.Count == 0)
                    return (compilation, true, errors);
            }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
            }
        }
        return (null!, false, errors);
    }

    private static (EvaluationResult Result, bool Success, List<string> Errors) RunFile(
        string filePath,
        ImmutableDictionary<string, Func<int>>? extGetters = null)
    {
        var tree = SyntaxTree.Load(filePath);
        var errors = tree.Diagnostics.Where(d => d.IsError).Select(d => d.Message).ToList();
        if (errors.Count > 0)
            return (null!, false, errors);

        var compilation = Compilation.Create(tree);
        var diag = compilation.Compile([]);
        foreach (var d in diag)
            errors.Add(d.Message);
        if (errors.Count > 0)
            return (null!, false, errors);

        var result = compilation.Evaluate(
            new MockOutputAdapter(), null,
            extGetters ?? [],
            new CancellationTokenSource().Token);
        return (result, !result.Diagnostics.HasErrors(), []);
    }

    #region 自动加载

    [Test]
    public void AutoLoad_LibFileLoadedAutomatically()
    {
        WriteLib("math.ecs", @"
FUNC double($x) : int
    RETURN $x * 2
ENDFUNC
");
        var mainPath = WriteMain("$r = double(21)");
        var (compilation, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.True, string.Join("; ", errors));
    }

    [Test]
    public void AutoLoad_MultipleLibFiles()
    {
        WriteLib("a.ecs", @"
FUNC add($a, $b) : int
    RETURN $a + $b
ENDFUNC
");
        WriteLib("b.ecs", @"
FUNC mul($a, $b) : int
    RETURN $a * $b
ENDFUNC
");
        var mainPath = WriteMain("$r = add(mul(3, 4), 5)");
        var (compilation, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.True, string.Join("; ", errors));
    }

    [Test]
    public void AutoLoad_NoLibDir_Succeeds()
    {
        // 不创建 lib 目录
        var mainPath = WriteMain("$x = 42");
        var (_, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.True, string.Join("; ", errors));
    }

    [Test]
    public void AutoLoad_EmptyLibDir_Succeeds()
    {
        Directory.CreateDirectory(_libDir);
        var mainPath = WriteMain("$x = 42");
        var (_, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.True, string.Join("; ", errors));
    }

    #endregion

    #region 库脚本解析限制

    [Test]
    public void LibParse_VariableDef_Succeeds()
    {
        WriteLib("vars.ecs", "$count = 10");
        var mainPath = WriteMain("$x = 1");
        var (_, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.True, string.Join("; ", errors));
    }

    [Test]
    public void LibParse_ConstantDef_Succeeds()
    {
        WriteLib("const.ecs", "_MAX = 100");
        var mainPath = WriteMain("$x = 1");
        var (_, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.True, string.Join("; ", errors));
    }

    [Test]
    public void LibParse_FuncDef_Succeeds()
    {
        WriteLib("func.ecs", @"
FUNC greet : int
    RETURN 42
ENDFUNC
");
        var mainPath = WriteMain("$x = greet()");
        var (_, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.True, string.Join("; ", errors));
    }

    [Test]
    public void LibParse_IfStatement_Fails()
    {
        WriteLib("bad.ecs", @"
IF 1
    $x = 1
ENDIF
");
        var mainPath = WriteMain("$x = 1");
        var (_, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.False);
        Assert.That(errors, Has.Some.Contains("库脚本只允许变量定义、常量定义和函数定义"));
    }

    [Test]
    public void LibParse_ForLoop_Fails()
    {
        WriteLib("bad.ecs", "FOR $i = 1 TO 5\nNEXT");
        var mainPath = WriteMain("$x = 1");
        var (_, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.False);
        Assert.That(errors, Has.Some.Contains("库脚本只允许变量定义、常量定义和函数定义"));
    }

    [Test]
    public void LibParse_WhileLoop_Fails()
    {
        WriteLib("bad.ecs", "WHILE 0\nEND");
        var mainPath = WriteMain("$x = 1");
        var (_, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.False);
        Assert.That(errors, Has.Some.Contains("库脚本只允许变量定义、常量定义和函数定义"));
    }

    [Test]
    public void LibParse_WaitStatement_Fails()
    {
        WriteLib("bad.ecs", "WAIT 100");
        var mainPath = WriteMain("$x = 1");
        var (_, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.False);
        Assert.That(errors, Has.Some.Contains("库脚本只允许变量定义、常量定义和函数定义"));
    }

    [Test]
    public void LibParse_KeyPress_Fails()
    {
        WriteLib("bad.ecs", "A 100");
        var mainPath = WriteMain("$x = 1");
        var (_, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.False);
        Assert.That(errors, Has.Some.Contains("库脚本只允许变量定义、常量定义和函数定义"));
    }

    #endregion

    #region 作用域隔离 - lib 不能访问主脚本变量

    [Test]
    public void LibScope_CannotAccessMainGlobal()
    {
        WriteLib("bad.ecs", @"
FUNC leak : int
    RETURN $mainVar
ENDFUNC
");
        var mainPath = WriteMain(@"
$mainVar = 42
$r = leak()
");
        var (_, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.False, "lib function should not access main script variable");
        Assert.That(errors, Has.Some.Contains("找不到变量"));
    }

    [Test]
    public void LibScope_CannotAccessMainConstant()
    {
        WriteLib("bad.ecs", @"
FUNC leak : int
    RETURN _MAIN_CONST
ENDFUNC
");
        var mainPath = WriteMain(@"
_MAIN_CONST = 99
$r = leak()
");
        var (_, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.False, "lib function should not access main script constant");
        Assert.That(errors, Has.Some.Contains("找不到变量"));
    }

    #endregion

    #region 作用域隔离 - lib 可以访问自身变量和常量

    [Test]
    public void LibScope_CanAccessLibOwnGlobal()
    {
        WriteLib("lib1.ecs", @"
_offset = 10
FUNC addOffset($x) : int
    RETURN $x + _offset
ENDFUNC
");
        var mainPath = WriteMain("$r = addOffset(5)");
        var (compilation, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.True, string.Join("; ", errors));

        var result = compilation.Evaluate(
            new MockOutputAdapter(), null, [],
            new CancellationTokenSource().Token);
        Assert.That(result.Result.AsInt(), Is.EqualTo(15));
    }

    [Test]
    public void LibScope_CanAccessLibVariable()
    {
        WriteLib("lib1.ecs", @"
$counter = 0
FUNC incCounter : int
    $counter = $counter + 1
    RETURN $counter
ENDFUNC
");
        var mainPath = WriteMain(@"
$a = incCounter()
$r = incCounter()
");
        var (compilation, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.True, string.Join("; ", errors));

        var result = compilation.Evaluate(
            new MockOutputAdapter(), null, [],
            new CancellationTokenSource().Token);
        Assert.That(result.Result.AsInt(), Is.EqualTo(2));
    }

    [Test]
    public void LibScope_LibFuncCanCallOtherLibFunc()
    {
        WriteLib("lib1.ecs", @"
FUNC square($x) : int
    RETURN $x * $x
ENDFUNC
FUNC sumSquares($a, $b) : int
    RETURN square($a) + square($b)
ENDFUNC
");
        var mainPath = WriteMain("$r = sumSquares(3, 4)");
        var (compilation, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.True, string.Join("; ", errors));

        var result = compilation.Evaluate(
            new MockOutputAdapter(), null, [],
            new CancellationTokenSource().Token);
        Assert.That(result.Result.AsInt(), Is.EqualTo(25));
    }

    [Test]
    public void LibScope_MultipleLibFiles_CrossRef()
    {
        WriteLib("a.ecs", @"
FUNC double($x) : int
    RETURN $x * 2
ENDFUNC
");
        WriteLib("b.ecs", @"
FUNC quad($x) : int
    RETURN double(double($x))
ENDFUNC
");
        var mainPath = WriteMain("$r = quad(3)");
        var (compilation, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.True, string.Join("; ", errors));

        var result = compilation.Evaluate(
            new MockOutputAdapter(), null, [],
            new CancellationTokenSource().Token);
        Assert.That(result.Result.AsInt(), Is.EqualTo(12));
    }

    #endregion

    #region 主脚本调用 lib 函数

    [Test]
    public void MainCanCall_LibFunction()
    {
        WriteLib("math.ecs", @"
FUNC add($a, $b) : int
    RETURN $a + $b
ENDFUNC
");
        var mainPath = WriteMain("$r = add(10, 20)");
        var (compilation, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.True, string.Join("; ", errors));

        var result = compilation.Evaluate(
            new MockOutputAdapter(), null, [],
            new CancellationTokenSource().Token);
        Assert.That(result.Result.AsInt(), Is.EqualTo(30));
    }

    [Test]
    public void MainCanCall_LibFunctionRecursive()
    {
        WriteLib("math.ecs", @"
FUNC fib($n) : int
    IF $n <= 1
        RETURN $n
    ENDIF
    RETURN fib($n - 1) + fib($n - 2)
ENDFUNC
");
        var mainPath = WriteMain("$r = fib(10)");
        var (compilation, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.True, string.Join("; ", errors));

        var result = compilation.Evaluate(
            new MockOutputAdapter(), null, [],
            new CancellationTokenSource().Token);
        Assert.That(result.Result.AsInt(), Is.EqualTo(55));
    }

    [Test]
    public void MainCannotAccess_LibVariable()
    {
        WriteLib("lib1.ecs", "$libVar = 100");
        var mainPath = WriteMain("$r = $libVar");
        var (_, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.False, "main script should not access lib variables");
        Assert.That(errors, Has.Some.Contains("找不到变量"));
    }

    [Test]
    public void MainCannotAccess_LibConstant()
    {
        WriteLib("lib1.ecs", "_LIB_CONST = 100");
        var mainPath = WriteMain("$r = _LIB_CONST");
        var (_, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.False, "main script should not access lib constants");
        Assert.That(errors, Has.Some.Contains("找不到变量"));
    }

    #endregion

    #region IsLib 标记

    [Test]
    public void IsLib_MainTree_IsFalse()
    {
        var tree = SyntaxTree.Parse("$x = 1");
        Assert.That(tree.IsLib, Is.False);
    }

    [Test]
    public void IsLib_LibTree_IsTrue()
    {
        var tree = SyntaxTree.Parse("$x = 1", isLib: true);
        Assert.That(tree.IsLib, Is.True);
    }

    #endregion

    #region lib 函数可调用内置函数

    [Test]
    public void LibScope_CanCallBuiltin()
    {
        WriteLib("lib1.ecs", @"
FUNC myRand : int
    RETURN RAND(100)
ENDFUNC
");
        var mainPath = WriteMain("$r = myRand()");
        var (compilation, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.True, string.Join("; ", errors));

        var result = compilation.Evaluate(
            new MockOutputAdapter(), null, [],
            new CancellationTokenSource().Token);
        Assert.That(result.Result.AsInt(), Is.InRange(0, 99));
    }

    #endregion
}