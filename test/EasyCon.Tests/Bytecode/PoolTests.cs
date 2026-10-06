using EasyCon.Core.Runner;
using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Script.Symbols;
using EasyCon.Tests.Support;
using System.Collections.Immutable;
using System.Text;

namespace EasyCon.Tests.Bytecode;

/// <summary>
/// 固定池行为（docs/ZeroAllocVm.md §4）：帧段/对象块耗尽 = ECS_ERR_POOL(18)——
/// **可预期的正常失败**，不是崩溃也不是 OOM；错误现场 pc/func 正确；释放后块可复用。
/// 池容量属宿主资源档案（§6）：C# 解释器无池（PC 有真实堆），同脚本双端错误码**允许资源性分歧**
  ///（C# 深递归触达 ERR_DEPTH(9)；C VM 帧段先满触达 ERR_POOL(18)）——语义条目仍由 corpus 锁定。
/// </summary>
[TestFixture]
public class PoolTests
{
    string _workDir = null!;
    string _vmBinary = null!;

    [OneTimeSetUp]
    public void BuildVm()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"EcsPool_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);
        var binary = CvmRunner.EnsureBuilt(_workDir);
        if (binary == null)
        {
            Assert.Ignore("无 cc 编译器，跳过 C VM 池测试");
            return;
        }
        _vmBinary = binary;
    }

    [OneTimeTearDown]
    public void Cleanup()
    {
        if (Directory.Exists(_workDir))
            Directory.Delete(_workDir, true);
    }

    const int EcsErrPool = 18;   // 与 ecs_vm.h ECS_ERR_POOL 一致（VmHeaderContractTests 锁头文件序）

    [Test]
    public void FramePoolExhausted_BigFrames_ReachPoolBeforeDepth()
    {
        // 大帧函数深递归：参考宿主帧段（128KB）在 512 深度前耗尽 → ECS_ERR_POOL（非崩溃非 OOM）
        var sb = new StringBuilder();
        sb.AppendLine("FUNC deep($n) : INT");
        for (int i = 0; i < 40; i++)
            sb.AppendLine($"    $t{i} = $n + {i}");
        // 行式语言：求和必须单行；深度计数器递减（避免算术回绕提前退出递归）
        sb.AppendLine("    $s = " + string.Join(" + ", Enumerable.Range(0, 40).Select(i => $"$t{i}")));
        sb.AppendLine("    IF $n > 0");
        sb.AppendLine("        RETURN 1 + deep($n - 1)");
        sb.AppendLine("    ENDIF");
        sb.AppendLine("    RETURN 0");
        sb.AppendLine("ENDFUNC");
        sb.AppendLine("deep(100000)");
        sb.AppendLine("PRINT \"unreachable\"");

        var result = Compilation.CompileSource(sb.ToString(), new CompileOptions { UseDiskCache = false, UseProcessCache = false });
        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty,
            string.Join("\n", result.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
        var image = result.Image!;
        Assert.That(image.Functions.Max(f => f.NSlots), Is.GreaterThan(40), "大帧函数已构造");

        // C# 解释器（无池）：一路递归到 ERR_DEPTH(9)
        var host = new EcxHost();
        int csCode = EcxInterpreter.Run(image, host);
        Assert.That(csCode, Is.EqualTo(EcxInterpreter.ERR_DEPTH), "C# 侧触达调用深度上限（池容量属宿主资源差异）");

        // C VM（帧段有限）：ECS_ERR_POOL(18)，stderr 带 func/pc 现场
        var (exitCode, _, _) = CvmRunner.Run(_vmBinary, EcsContainer.WriteImage(image), "pool-frame", _workDir);
        Assert.That(exitCode, Is.EqualTo(EcsErrPool),
            "C VM 帧段耗尽应报 ECS_ERR_POOL（可预期的正常失败）");
    }

    [Test]
    public void ObjectPoolExhausted_ManyLiveArrays_ReportedAsPool()
    {
        // 大量存活小数组耗尽对象块：ECS_ERR_POOL（非崩溃非 OOM）
        var sb = new StringBuilder();
        sb.AppendLine("$keep = [\"a\"]");
        sb.AppendLine("FOR $i = 1 TO 20000");
        sb.AppendLine("    $keep = APPEND($keep, \"abcd\")");
        sb.AppendLine("NEXT");
        sb.AppendLine("PRINT LEN($keep)");

        var result = Compilation.CompileSource(sb.ToString(), new CompileOptions { UseDiskCache = false, UseProcessCache = false });
        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty);
        var image = result.Image!;

        var (exitCode, _, _) = CvmRunner.Run(_vmBinary, EcsContainer.WriteImage(image), "pool-obj", _workDir);
        Assert.That(exitCode, Is.EqualTo(EcsErrPool),
            "对象块耗尽应报 ECS_ERR_POOL（1024 块上限 < 20000 存活数组）");
    }

    [Test]
    public void PoolReleased_BlocksAreReusable()
    {
        // 释放后复用：循环内分配/释放大量对象不累积（C VM 全程零错误完成）
        var sb = new StringBuilder();
        sb.AppendLine("FOR $i = 1 TO 5000");
        sb.AppendLine("    $t = [1, 2, 3]");
        sb.AppendLine("    $u = $t");
        sb.AppendLine("    $t = [4]");
        sb.AppendLine("    $u = [5]");
        sb.AppendLine("NEXT");
        sb.AppendLine("PRINT \"reused\"");

        var result = Compilation.CompileSource(sb.ToString(), new CompileOptions { UseDiskCache = false, UseProcessCache = false });
        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty);
        var image = result.Image!;

        // C# 侧：堆有界（复用自由链）
        var vmHost = new EcxHost();
        int code = EcxInterpreter.RunWithVm(image, vmHost, out var vm);
        Assert.That(code, Is.EqualTo(EcxInterpreter.OK));
        Assert.That(vm.HeapHighWater, Is.LessThan(40), $"存活对象应有界（实测 {vm.HeapHighWater}）");

        // C VM 侧：全程完成（块复用，不触池上限）
        var (exitCode, stdout, stderr) = CvmRunner.Run(_vmBinary, EcsContainer.WriteImage(image), "pool-reuse", _workDir);
        Assert.That(exitCode, Is.EqualTo(0), $"stderr={stderr}");
        Assert.That(CvmRunner.SplitLines(stdout), Is.EqualTo(new[] { "reused" }));
    }
}
