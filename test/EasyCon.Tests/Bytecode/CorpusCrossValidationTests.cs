using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Tests.Support;

namespace EasyCon.Tests.Bytecode;

/// <summary>
/// 双实现对拍语料（docs/VmSemanticContract.md §五 验证体系）：
/// corpus/ 下每用例 = &lt;名&gt;.ecs + &lt;名&gt;.expected（期望输出行，逐行）+ 可选 &lt;名&gt;.events（域事件）。
///
/// 流程：跑「CompileSource → EcxImage → EcxInterpreter」断言输出/事件与期望一致；
/// 有 cc 时同一镜像交 C VM（ci/build-vm.sh 同机制现场编译 ecs-vm）做三方对拍；无 cc 跳过 C VM 侧。
///
/// .events 格式与 EcxHost.EnableRecording 一致（KEY n d / KEYST n s / STICK s x y / STICKC s x y d /
/// WAIT ms / AMIIBO n / BEEP f d），按标签分组存储（KEY → KEYST → STICK → STICKC → WAIT → AMIIBO → BEEP），
/// 与 C VM stderr TSV 的分组对拍语义一致。
///
/// 新增语义用例 = 加两个文件 + 跑一次。双端不一致时先判定哪端错：修实现而非改期望（期望即规格）。
/// C VM 基建（构建/执行/TSV 解析）见 Support/CvmRunner；录制宿主见 Support/EcsTestHost。
/// </summary>
[TestFixture]
public class CorpusCrossValidationTests
{
    static string? _vmBinary;
    static string _workDir = "";

    [OneTimeSetUp]
    public void BuildVm()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"EcsCorpus_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);
        _vmBinary = CvmRunner.EnsureBuilt(_workDir);
        // 无 cc：C VM 侧跳过（§5 V7），解释器侧语料照常验证（逐用例 Ignore）
    }

    [OneTimeTearDown]
    public void Cleanup()
    {
        if (Directory.Exists(_workDir))
            Directory.Delete(_workDir, true);
    }

    static IEnumerable<string> CorpusCases() => CorpusAssert.CorpusCases();

    [TestCaseSource(nameof(CorpusCases))]
    public void Corpus_Case(string ecsPath)
    {
        var tag = Path.GetFileNameWithoutExtension(ecsPath);
        var expectedLinesPath = Path.Combine(CorpusAssert.CorpusDir(), tag + ".expected");
        var expectedLines = File.Exists(expectedLinesPath) ? File.ReadAllLines(expectedLinesPath) : [];   // 纯事件用例可无输出期望
        var eventsPath = Path.Combine(CorpusAssert.CorpusDir(), tag + ".events");
        var expectedEvents = File.Exists(eventsPath) ? File.ReadAllLines(eventsPath) : [];

        // ① 统一编译链路 → EcxInterpreter
        var result = Compilation.CompileSource(File.ReadAllText(ecsPath), new CompileOptions { UseDiskCache = false });
        Assert.That(result.Diagnostics.Where(d => d.IsError).ToList(), Is.Empty,
            $"[{tag}] 编译失败：" + string.Join("\n", result.Diagnostics));

        var host = EcsTestHost.CreateRecording();
        var code = EcxInterpreter.Run(result.Image!, host);
        Assert.That(code, Is.EqualTo(EcxInterpreter.OK), $"[{tag}] 解释器执行失败：{code}");
        Assert.That(host.Lines, Is.EqualTo(expectedLines), $"[{tag}] 输出行与期望不一致");
        Assert.That(EcsTestHost.AllEvents(host), Is.EqualTo(expectedEvents), $"[{tag}] 事件与期望不一致");

        // ② 同一镜像 → C VM（有 cc 时三方对拍）
        if (_vmBinary == null)
            Assert.Ignore("无 cc 编译器，仅完成解释器侧语料验证（C VM 侧跳过）");

        var (exitCode, stdout, stderr) = CvmRunner.Run(_vmBinary, EcxWriter.Write(result.Image!), tag, _workDir);
        Assert.That(exitCode, Is.EqualTo(0), $"[{tag}] C VM 退出码：{exitCode}；stderr={stderr}");
        Assert.That(CvmRunner.SplitLines(stdout), Is.EqualTo(expectedLines),
            $"[{tag}] C VM 与期望输出不一致（双端不一致时先判定哪端错，修实现而非改期望）");
        Assert.That(CvmRunner.ParseEventTsv(stderr), Is.EqualTo(expectedEvents), $"[{tag}] C VM 事件 TSV 与期望不一致");
    }
}