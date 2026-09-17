using EasyCon.Core.Runner;
using EasyCon.Script;
using EasyCon.Script.Ssa;
using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using EasyCon.Tests.Support;
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
        var path = Path.Combine(_libDir, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, code);
        return path;
    }

    private static Value EvalResult(CompileResult compileResult)
    {
        if (compileResult.Image == null)
            throw new Exception($"编译错误: {string.Join("; ", compileResult.Diagnostics.Where(d => d.IsError).Select(d => d.Message))}");
        return EcxVm.Run(compileResult.Image!, EcsTestHost.Capabilities(new MockOutputAdapter()),
            new CancellationTokenSource().Token, [], compileResult.NativeSymbols);
    }

    private static (CompileResult Result, bool Success, List<string> Errors) CompileFile(string filePath)
    {
        var result = Compilation.CompileFile(filePath, new CompileOptions { UseDiskCache = false });
        var errors = result.Diagnostics.Where(d => d.IsError).Select(d => d.Message).ToList();
        return (result, errors.Count == 0, errors);
    }

    private static (Value Result, bool Success, List<string> Errors) RunFile(
        string filePath,
        ImmutableDictionary<string, Func<int>>? extGetters = null)
    {
        var (result, success, errors) = CompileFile(filePath);
        if (!success)
            return (Value.Void, false, errors);
        return (EvalResult(result), true, []);
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

    #region 库模块顶层语句（模块语义：v1 lib 白名单退役，顶层语句合法化为 &lt;init:module&gt;）

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
    public void LibInit_TopLevelStatements_BecomeInit()
    {
        // 模块语义（ModuleSystem.md §6）：lib 顶层语句进入 &lt;init:module&gt;，链接序先于 main 执行
        WriteLib("init.ecs", "$cnt = 0\n$cnt = $cnt + 1\n");
        var mainPath = WriteMain("$x = 1\nRETURN $x");
        var (result, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.True, string.Join("; ", errors));
        var init = result.Artifacts.Single(a => a.Name == "init");
        Assert.That(init.HasInit, Is.True, "顶层语句应置 HasInit（&lt;init:module&gt;）");
    }

    [Test]
    public void LibInit_WaitAndKeyStatements_Compile()
    {
        WriteLib("init.ecs", "WAIT 1\nA 1\n");
        var mainPath = WriteMain("$x = 1\nRETURN $x");
        var (result, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.True, string.Join("; ", errors));
        Assert.That(result.Image!.KeyAction, Is.True, "lib init 的按键应并集进 KeyAction");
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

    #region 作用域隔离 - lib 可以访问自身常量

    [Test]
    public void LibScope_CanAccessLibOwnConstant()
    {
        WriteLib("lib1.ecs", @"
_offset = 10
FUNC addOffset($x) : int
    RETURN $x + _offset
ENDFUNC
");
        var mainPath = WriteMain("$r = addOffset(5)\nRETURN $r");
        var (compilation, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.True, string.Join("; ", errors));

        var result = EvalResult(compilation);
        Assert.That(result.AsInt(), Is.EqualTo(15));
    }

    #endregion

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
        var mainPath = WriteMain("$r = sumSquares(3, 4)\nRETURN $r");
        var (compilation, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.True, string.Join("; ", errors));

        var result = EvalResult(compilation);
        Assert.That(result.AsInt(), Is.EqualTo(25));
    }

    [Test]
    public void LibScope_MultipleLibFiles_CrossRef()
    {
        // 模块语义：跨模块调用需显式 IMPORT（b IMPORT a；lib 内 import 解析到自身 lib/ 子目录）
        WriteLib("lib/a.ecs", @"
FUNC double($x) : int
    RETURN $x * 2
ENDFUNC
");
        WriteLib("b.ecs", @"
IMPORT ""a.ecs""
FUNC quad($x) : int
    RETURN double(double($x))
ENDFUNC
");
        var mainPath = WriteMain("$r = quad(3)\nRETURN $r");
        var (compilation, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.True, string.Join("; ", errors));

        var result = EvalResult(compilation);
        Assert.That(result.AsInt(), Is.EqualTo(12));
    }

    #region 主脚本调用 lib 函数

    [Test]
    public void MainCanCall_LibFunction()
    {
        WriteLib("math.ecs", @"
FUNC add($a, $b) : int
    RETURN $a + $b
ENDFUNC
");
        var mainPath = WriteMain("$r = add(10, 20)\nRETURN $r");
        var (compilation, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.True, string.Join("; ", errors));

        var result = EvalResult(compilation);
        Assert.That(result.AsInt(), Is.EqualTo(30));
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
        var mainPath = WriteMain("$r = fib(10)\nRETURN $r");
        var (compilation, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.True, string.Join("; ", errors));

        var result = EvalResult(compilation);
        Assert.That(result.AsInt(), Is.EqualTo(55));
    }

    [Test]
    public void LibGlobal_NotAccessibleFromMain()
    {
        // main 引用 lib 全局变量 → 编译错误（变量不可见）
        WriteLib("lib1.ecs", "_data = 42");
        var mainPath = WriteMain("$r = _data");
        var (compilation, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.False);
        Assert.That(errors, Has.Some.Contains("找不到变量"));
    }

    [Test]
    public void LibGlobal_SameNameMainGlobal_NoConflict()
    {
        // main 声明与 lib 同名全局变量 → 不冲突（lib 变量不暴露）
        WriteLib("lib1.ecs", "_offset = 10");
        var mainPath = WriteMain("_offset = 20\nRETURN _offset");
        var (compilation, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.True, string.Join("; ", errors));
        var result = EvalResult(compilation);
        Assert.That(result.AsInt(), Is.EqualTo(20));
    }

    [Test]
    public void LibGlobal_LibFuncCanAccessOwnGlobal()
    {
        // lib 函数访问 lib 自身全局变量（回归）
        WriteLib("lib1.ecs", @"
_offset = 10
FUNC addOffset($x) : int
    RETURN $x + _offset
ENDFUNC
");
        var mainPath = WriteMain("$r = addOffset(5)\nRETURN $r");
        var (compilation, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.True, string.Join("; ", errors));

        var result = EvalResult(compilation);
        Assert.That(result.AsInt(), Is.EqualTo(15));
    }

    [Test]
    public void LibGlobal_MainFuncLocalCanShadowName()
    {
        // main 函数内声明与 lib 全局同名的局部变量 → 允许
        WriteLib("lib1.ecs", @"
_data = 99
FUNC getLibData() : int
    RETURN _data
ENDFUNC
");
        var mainPath = WriteMain(@"
FUNC mainFunc() : int
    $data = 1
    RETURN $data
ENDFUNC
$r = mainFunc()
RETURN $r");
        var (compilation, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.True, string.Join("; ", errors));

        var result = EvalResult(compilation);
        Assert.That(result.AsInt(), Is.EqualTo(1));
    }

    [Test]
    public void LibGlobal_VarIsolation()
    {
        // lib 中 $counter 是可变全局变量，lib 函数可读写，main 不可见
        WriteLib("lib1.ecs", @"
$counter = 0
FUNC increment() : int
    $counter = $counter + 1
    RETURN $counter
ENDFUNC
FUNC getCounter() : int
    RETURN $counter
ENDFUNC
");
        var mainPath = WriteMain(@"
$r = increment()
$r = increment()
$r = getCounter()
RETURN $r");
        var (compilation, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.True, string.Join("; ", errors));

        var result = EvalResult(compilation);
        Assert.That(result.AsInt(), Is.EqualTo(2));
    }

    [Test]
    public void LibGlobal_VarNoConflictWithMain()
    {
        // main 声明与 lib 同名 $ 全局变量 → 不冲突（lib 变量不暴露）
        WriteLib("lib1.ecs", "$total = 0");
        var mainPath = WriteMain("$total = 10\nRETURN $total");
        var (compilation, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.True, string.Join("; ", errors));
        var result = EvalResult(compilation);
        Assert.That(result.AsInt(), Is.EqualTo(10));
    }

    [Test]
    public void LibGlobal_VarNotAccessibleFromMain()
    {
        // main 引用 lib $ 全局变量 → 编译错误（变量不可见）
        WriteLib("lib1.ecs", "$count = 5");
        var mainPath = WriteMain("$r = $count");
        var (compilation, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.False);
        Assert.That(errors, Has.Some.Contains("找不到变量"));
    }

    [Test]
    public void LibGlobal_VarInit_ArbitraryExpressions()
    {
        // 模块语义：lib 顶层赋值是 &lt;init&gt; 语句，非常量表达式合法（运行期求值）
        WriteLib("lib1.ecs", "$val = 1 + 2\nFUNC get() : int\n RETURN $val\nENDFUNC");
        var mainPath = WriteMain("RETURN get()");
        var (result, success, errors) = CompileFile(mainPath);
        Assert.That(success, Is.True, string.Join("; ", errors));
        Assert.That(EvalResult(result).AsInt(), Is.EqualTo(3));
    }

    [Test]
    public void LibGlobal_VarNonConstantInit_RunsAtInit()
    {
        WriteLib("lib1.ecs", "$v = RAND(10)\nFUNC get() : int\n RETURN $v\nENDFUNC");
        var mainPath = WriteMain("RETURN get()");
        var (result, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.True, string.Join("; ", errors));
        Assert.That(EvalResult(result).AsInt(), Is.InRange(0, 9));
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
        var mainPath = WriteMain("$r = myRand()\nRETURN $r");
        var (compilation, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.True, string.Join("; ", errors));

        var result = EvalResult(compilation);
        Assert.That(result.AsInt(), Is.InRange(0, 99));
    }

    #endregion

    #region import 语句加载 lib

    [Test]
    public void Import_LoadsLibFile()
    {
        WriteLib("math.ecs", @"
FUNC triple($x) : int
    RETURN $x * 3
ENDFUNC
");
        var mainPath = WriteMain(@"IMPORT ""math.ecs""
$r = triple(7)
RETURN $r");
        var (compilation, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.True, string.Join("; ", errors));

        var result = EvalResult(compilation);
        Assert.That(result.AsInt(), Is.EqualTo(21));
    }

    [Test]
    public void Import_MultipleImports()
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
        var mainPath = WriteMain(@"IMPORT ""a.ecs""
IMPORT ""b.ecs""
$r = add(mul(3, 4), 5)
RETURN $r");
        var (compilation, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.True, string.Join("; ", errors));

        var result = EvalResult(compilation);
        Assert.That(result.AsInt(), Is.EqualTo(17));
    }

    [Test]
    public void Import_AndAutoLoad_Coexist()
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
        // 只 import a.ecs，b.ecs 通过自动加载
        var mainPath = WriteMain(@"IMPORT ""a.ecs""
$r = add(mul(3, 4), 1)
RETURN $r");
        var (compilation, success, errors) = CompileFile(mainPath);

        Assert.That(success, Is.True, string.Join("; ", errors));

        var result = EvalResult(compilation);
        Assert.That(result.AsInt(), Is.EqualTo(13));
    }

    #endregion
}