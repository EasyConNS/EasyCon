using EasyCon.Core.Runner;
using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Tests.Support;

namespace EasyCon.Tests.Bytecode;

/// <summary>
/// 随机脚本差分验证（wasm-smith/regalloc2 式「生成 → 双实现对拍」）：
/// ScriptFuzzer 按种子生成确定性程序，统一链路编译后由
/// EcxInterpreter（C# 语义实现）与 C 虚拟机 ecs-vm（有 cc 时）独立执行，
/// 输出行与事件全量对拍；同时扫描行号表不变量。
///
/// 绊线语义：KnownBadSeeds 已清零。名单外新失败 → 本类红灯（缺陷扩散）；
/// 若未来重新启用隔离名单，名单内种子转绿同样红灯（提示缺陷已修，移出名单并补充全量验证）。
/// </summary>
[TestFixture]
public class FuzzCrossValidationTests
{
    static string? _vmBinary;
    static string _workDir = "";

    /// <summary>
    /// 已知坏种子（优化器缺陷触发表，fuzz 实证）。
    /// 已清零：builder 幽灵 sink 边（CONTINUE/BREAK 死续块被 EmitIf/For 接成合并点幽灵前驱，
    /// SealBlock 沿幽灵边穿透产生 0 臂占位 φ）、DCE 死 φ 不删（臂 Uses 递减但 φ 残留 →
    /// 悬空 ConstInt）、SCCP 常量格依赖 home 块（折叠后不可达块常量冻结循环 φ → 条件折叠成恒真）。
    /// 名单外新失败 = 红灯（缺陷扩散）。
    /// </summary>
#if DEBUG
    static readonly int[] KnownBadSeeds =
    [
    ];
#else
    static readonly int[] KnownBadSeeds =
    [
    ];
#endif

    [OneTimeSetUp]
    public void BuildVm()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"EcsFuzz_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);
        _vmBinary = CvmRunner.EnsureBuilt(_workDir);
        // 无 cc：C VM 侧跳过（与 CorpusCrossValidationTests 同策略），解释器侧照常执行
    }

    [OneTimeTearDown]
    public void Cleanup()
    {
        if (Directory.Exists(_workDir))
            Directory.Delete(_workDir, true);
    }

    static IEnumerable<int> Seeds() => Enumerable.Range(1, 16).Except(KnownBadSeeds);

    static CompileResult CompileOrDump(string source, string tag)
    {
        var result = Compilation.CompileSource(source, new CompileOptions { UseDiskCache = false });
        Assert.That(result.Diagnostics.Where(d => d.IsError).ToList(), Is.Empty,
            $"[{tag}] 生成器产出了编译失败的程序（生成器缺陷或优化器缺陷）：\n{source}\n诊断：{string.Join("\n", result.Diagnostics)}");
        Assert.That(result.Image, Is.Not.Null, $"[{tag}] 镜像为空");
        return result;
    }

    static void AssertLineTableInvariants(int seed, EcxImage image)
    {
        foreach (var fn in image.Functions)
        {
            for (int i = 2; i < fn.LineTable.Count; i += 2)
                Assert.That(fn.LineTable[i], Is.GreaterThan(fn.LineTable[i - 2]),
                    $"[seed {seed}] {fn.Module}/{fn.Name} 行号表 pc 非严格递增");
            for (int i = 1; i < fn.LineTable.Count; i += 2)
                Assert.That(fn.LineTable[i], Is.GreaterThanOrEqualTo(1),
                    $"[seed {seed}] {fn.Module}/{fn.Name} 行号非法");
        }
    }

    [TestCaseSource(nameof(Seeds))]
    public void Fuzz_Differential_BySeed(int seed)
    {
        var source = ScriptFuzzer.Generate(seed);
        var result = CompileOrDump(source, $"seed {seed}");

        // ① 解释器侧：执行成功 + 输出/事件
        var host = EcsTestHost.CreateRecording();
        var code = EcxInterpreter.Run(result.Image!, host);
        Assert.That(code, Is.EqualTo(EcxInterpreter.OK),
            $"[seed {seed}] 解释器执行失败：{code}\n{source}");

        // ② 行号表不变量
        AssertLineTableInvariants(seed, result.Image!);

        // ③ C VM 对拍（有 cc 时）
        if (_vmBinary == null)
            Assert.Ignore($"[seed {seed}] 无 cc 编译器，仅完成解释器侧验证（C VM 侧跳过）");

        var (exitCode, stdout, stderr) = CvmRunner.Run(_vmBinary, EcxWriter.Write(result.Image!), $"fuzz{seed}", _workDir);
        Assert.That(exitCode, Is.EqualTo(0),
            $"[seed {seed}] C VM 退出码 {exitCode}；stderr={stderr}\n{source}");
        Assert.That(CvmRunner.SplitLines(stdout), Is.EqualTo(host.Lines),
            $"[seed {seed}] C VM 与解释器输出不一致\n{source}");
        Assert.That(CvmRunner.ParseEventTsv(stderr), Is.EqualTo(EcsTestHost.AllEvents(host)),
            $"[seed {seed}] C VM 与解释器事件不一致\n{source}");
    }

    /// <summary>
    /// 隔离名单盯守：名单内种子必须仍以「已知签名」失败（编译错误或超时挂起）。
    /// 任一种子转绿 → 红灯提示移出名单；失败签名变化 → 红灯提示新缺陷。
    /// </summary>
    [Test]
    public void Fuzz_KnownBadSeeds_Quarantine()
    {
        var unexpected = new List<string>();
        foreach (var seed in KnownBadSeeds)
        {
            var source = ScriptFuzzer.Generate(seed);
            var result = Compilation.CompileSource(source, new CompileOptions { UseDiskCache = false });
            var hasCompileError = result.Diagnostics.Any(d => d.IsError);
            if (hasCompileError)
                continue;   // 已知签名①：编码/优化缺陷 → 编译错误

            var host = EcsTestHost.CreateRecording();
            var runTask = Task.Run(() => EcxInterpreter.Run(result.Image!, host));
            if (!runTask.Wait(2000))
                continue;   // 已知签名②：静默死循环误编译

            unexpected.Add($"seed {seed} 现在能正常编译执行了（行数 {host.Lines.Count}）——" +
                           "缺陷已修复？请将其移出 KnownBadSeeds 并补充全量差分验证");
        }
        Assert.That(unexpected, Is.Empty, string.Join("\n", unexpected));
    }

    [Test]
    public void Fuzz_BulkInterpreterSweep_192Seeds()
    {
        // 无进程派生的大样本扫描（隔离名单外种子全量执行 + 行号表不变量）
        var failures = new List<string>();
        var compiled = 0;
        for (int seed = 100; seed < 292; seed++)
        {
            if (KnownBadSeeds.Contains(seed))
                continue;
            var source = ScriptFuzzer.Generate(seed);
            var result = Compilation.CompileSource(source, new CompileOptions { UseDiskCache = false });
            var errors = result.Diagnostics.Where(d => d.IsError).ToList();
            if (errors.Count > 0)
            {
                failures.Add($"[seed {seed}] 编译失败：{string.Join(" | ", errors.Select(d => d.Message))}\n{source}");
                continue;
            }

            var host = EcsTestHost.CreateRecording();
            var runTask = Task.Run(() => EcxInterpreter.Run(result.Image!, host));
            if (!runTask.Wait(5000))
            {
                failures.Add($"[seed {seed}] 解释器 5s 未结束（疑似死循环误编译）\n{source}");
                continue;
            }
            Assert.That(runTask.Result, Is.EqualTo(EcxInterpreter.OK),
                $"[seed {seed}] 解释器执行失败：{runTask.Result}\n{source}");

            foreach (var fn in result.Image!.Functions)
                Assert.That(fn.LineTable.Count % 2, Is.EqualTo(0),
                    $"[seed {seed}] {fn.Module}/{fn.Name} 行号表长度应为偶数（[pc,line] 交错）");
            compiled++;
        }
        Assert.That(failures, Is.Empty, string.Join("\n====\n", failures));
        Assert.That(compiled, Is.GreaterThan(170), "有效编译数异常");
    }
}