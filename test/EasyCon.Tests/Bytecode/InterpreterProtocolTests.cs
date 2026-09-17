using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Script.Syntax;
using EasyCon.Script.Text;
using System.Collections.Immutable;

namespace EasyCon.Tests.Bytecode;

/// <summary>
/// 执行协议单测（docs/VmSemanticContract.md S-18）—— EcxInterpreter 执行协议（C# 语义参考实现）：
/// YIELD 预算切片续跑、取消（CANCELLED）、错误现场（错误码 + ErrorFunc/ErrorPc，对齐 C VM ecs_vm_error_location）。
/// 调用深度上限仅 C VM 承担（ECS_MAX_CALL_DEPTH → ECS_ERR_DEPTH，见 CvmCrossValidationTests.Cvm_CallDepthLimit）；
/// C# 解释器帧为堆分配，与金标准 SsaEvaluator（.NET 调用栈）一致不设限。
/// </summary>
[TestFixture]
public class InterpreterProtocolTests
{
    static EcxImage CompileImage(string source, string tag)
    {
        var result = Compilation.CompileSource(source, new CompileOptions { UseDiskCache = false });
        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty,
            string.Join("\n", result.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
        return result.Image!;
    }

    [Test]
    public void Yield_BudgetSlice_ContinuesToCorrectResult()
    {
        // 40 万次循环 ≈ 数百万步，远超 100 万步/切片预算：Run 吸收 YIELD 续跑，跨切片状态不丢
        var image = CompileImage("""
            $sum = 0
            FOR $i = 1 TO 400000
                $sum += 1
            NEXT
            PRINT $sum
            """, "yield");

        var host = new EcxHost();
        host.EnableRecording();
        var code = EcxInterpreter.Run(image, host);
        Assert.That(code, Is.EqualTo(EcxInterpreter.OK), "Run 应吸收 YIELD 切片直到完成");
        Assert.That(host.Lines, Is.EqualTo(new[] { "400000" }), "跨切片续跑后累加结果正确");
    }

    [Test]
    public void Cancel_BeforeStart_ReturnsCancelled()
    {
        var image = CompileImage("PRINT \"x\"\n", "cancel-pre");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var code = EcxInterpreter.Run(image, new EcxHost(), cts.Token);
        Assert.That(code, Is.EqualTo(EcxInterpreter.CANCELLED), "已取消的 token 应立即返回 CANCELLED");
    }

    [Test]
    public void Cancel_MidRun_AtHostNative_ReturnsCancelled()
    {
        // WAIT 原生回调内取消 → 宿主调用返回后立即感知（ExecOther 域操作返回取消状态）；
        // 取消点之后的指令不得执行
        var image = CompileImage("WAIT 500\nPRINT \"after-wait\"\n", "cancel-mid");
        using var cts = new CancellationTokenSource();
        var host = new EcxHost();
        host.EnableRecording();
        host.WaitMs = _ => cts.Cancel();   // EnableRecording 会覆写 WaitMs，注入须在其后
        var code = EcxInterpreter.Run(image, host, cts.Token);
        Assert.That(code, Is.EqualTo(EcxInterpreter.CANCELLED), "WAIT 后应立即取消");
        Assert.That(host.Lines, Is.Empty, "取消点之后的指令不应执行");
    }

    [Test]
    public void ErrorSite_DivZero_ReportsFuncAndInstructionPc()
    {
        var image = CompileImage("$x = 1 / 0\nPRINT $x\n", "site-div");
        var code = EcxInterpreter.Run(image, new EcxHost(), out int errorFunc, out int errorPc);

        Assert.That(code, Is.EqualTo(EcxInterpreter.ERR_DIVZERO));
        Assert.That(errorFunc, Is.InRange(0, image.Functions.Count - 1), "错误现场应指向有效函数");
        Assert.That(image.Functions[errorFunc].Name, Is.EqualTo("<main>"),
            "除零应发生在入口函数 <main>（链接后 $eval 本体前插 init 序列并更名，错误现场指向实际失败函数）");
        Assert.That(errorPc, Is.InRange(0, image.Functions[errorFunc].Code.Count - 1));
        var failingOp = (EcsOpcode)(image.Functions[errorFunc].Code[errorPc] & 0xFF);
        Assert.That(failingOp, Is.EqualTo(EcsOpcode.DivI), "错误现场应指向除法指令本身（与 C VM 语义一致）");
    }

    [Test]
    public void ErrorSite_IndexOutOfRange_ReportsIndex()
    {
        var image = CompileImage("$a = [1, 2, 3]\n$x = $a[5]\nPRINT $x\n", "site-idx");
        var code = EcxInterpreter.Run(image, new EcxHost(), out _, out _);
        Assert.That(code, Is.EqualTo(EcxInterpreter.ERR_INDEX));
    }
}