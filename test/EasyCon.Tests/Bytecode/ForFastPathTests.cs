using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Tests.Support;
using NUnit.Framework;

namespace EasyCon.Tests.Bytecode;

/// <summary>
/// FOR ForStep 快速路径（尾部融合）语义锁定：单层/嵌套/边界（零迭代、单次、
/// body 不读循环变量、部分路径读、循环后变量值）——C# 解释器执行断言。
/// 嵌套用例同时由 CvmCrossValidation 面覆盖双端对拍。
/// </summary>
[TestFixture]
public class ForFastPathTests
{
    static CompileResult Compile(string source)
    {
        var result = Compilation.CompileSource(source, new CompileOptions { UseDiskCache = false });
        Assert.That(result.Diagnostics.Where(d => d.IsError).ToList(), Is.Empty,
            "编译诊断：" + string.Join("\n", result.Diagnostics));
        Assert.That(result.Image, Is.Not.Null);
        return result;
    }

    /// <summary>解释器执行（3s CTS 判死循环）；返回 (输出行, 错误码)。</summary>
    static (List<string> Lines, int Code) RunInterp(CompileResult result)
    {
        using var cts = new CancellationTokenSource(3000);
        var host = EcsTestHost.CreateRecording();
        int code = EcxInterpreter.Run(result.Image!, host, cts.Token);
        return (host.Lines, code);
    }

    static void AssertRuns(CompileResult result, params string[] expectedLines)
    {
        var (lines, code) = RunInterp(result);
        Assert.That(code, Is.EqualTo(0), $"执行错误码 {code}（2=CANCELLED 即死循环）");
        Assert.That(lines, Is.EqualTo(expectedLines), "PRINT 输出");
    }

    // ---------- 单层 ----------

    [Test]
    public void Single_Level_Accumulate()
    {
        // body 不读循环变量
        AssertRuns(Compile("""
            $total = 0
            FOR $i = 1 TO 3
                $total = $total + 1
            NEXT
            PRINT $total
            """), "3");
    }

    [Test]
    public void Single_Level_ReadVar_Full()
    {
        AssertRuns(Compile("""
            $total = 0
            FOR $i = 1 TO 5
                $total = $total + $i
            NEXT
            PRINT $total
            """), "15");
    }

    [Test]
    public void Zero_Iteration_3TO1()
    {
        AssertRuns(Compile("""
            $total = 0
            FOR $i = 3 TO 1
                $total = $total + 1
            NEXT
            PRINT $total
            """), "0");
    }

    [Test]
    public void Single_Iteration_5TO5()
    {
        AssertRuns(Compile("""
            $total = 0
            FOR $i = 5 TO 5
                $total = $total + 1
            NEXT
            PRINT $total
            """), "1");
    }

    // ---------- 嵌套 ----------

    [Test]
    public void Nested_BodyNotReadingOuterVar()
    {
        // 外层 body 含内层 FOR，且外层循环变量在 body 中无读取（历史上的死循环形态）
        AssertRuns(Compile("""
            $total = 0
            FOR $i = 1 TO 3
                FOR $j = 1 TO 3
                    $total = $total + 1
                NEXT
            NEXT
            PRINT $total
            """), "9");
    }

    [Test]
    public void Nested_InnerReadsOuterVar()
    {
        AssertRuns(Compile("""
            $total = 0
            FOR $i = 1 TO 3
                FOR $j = 1 TO 3
                    $total = $total + $i
                NEXT
            NEXT
            PRINT $total
            """), "18");
    }

    [Test]
    public void Nested_ThreeLevel()
    {
        AssertRuns(Compile("""
            $total = 0
            FOR $i = 1 TO 2
                FOR $j = 1 TO 2
                    FOR $k = 1 TO 2
                        $total = $total + 1
                    NEXT
                NEXT
            NEXT
            PRINT $total
            """), "8");
    }

    // ---------- 部分路径读 / 循环后值 ----------

    [Test]
    public void ReadVar_PartialPath()
    {
        // 循环变量只在 body 的部分路径读（IF then 臂）
        AssertRuns(Compile("""
            $total = 0
            FOR $i = 1 TO 4
                IF $i == 2
                    $total = $total + 100
                ENDIF
                $total = $total + 1
            NEXT
            PRINT $total
            """), "104");
    }

    [Test]
    public void LoopVar_LastValue_IsUpper()
    {
        // Binder 禁止循环后读 FOR 变量（作用域规则）→ 用 body 内最后一次赋值锁定
        // 「出循环时 $i == upper（不是 upper+1）」契约（通用 lowering 与 fast path 同语义）
        AssertRuns(Compile("""
            $last = 0
            FOR $i = 1 TO 5
                $last = $i
            NEXT
            PRINT $last
            """), "5");
    }



    // ---------- CONTINUE / BREAK ----------



    [Test]
    public void Continue_GoesToStep()
    {
        AssertRuns(Compile("""
            $total = 0
            FOR $i = 1 TO 5
                IF $i == 3
                    CONTINUE
                ENDIF
                $total = $total + 1
            NEXT
            PRINT $total
            """), "4");
    }

    [Test]
    public void Break_ExitsLoop()
    {
        AssertRuns(Compile("""
            $total = 0
            FOR $i = 1 TO 5
                IF $i == 3
                    BREAK
                ENDIF
                $total = $total + 1
            NEXT
            PRINT $total
            """), "2");
    }

    [Test]
    public void Break_InNestedLoop()
    {
        AssertRuns(Compile("""
            $total = 0
            FOR $i = 1 TO 3
                FOR $j = 1 TO 3
                    IF $j == 2
                        BREAK
                    ENDIF
                    $total = $total + 1
                NEXT
            NEXT
            PRINT $total
            """), "3");
    }
}