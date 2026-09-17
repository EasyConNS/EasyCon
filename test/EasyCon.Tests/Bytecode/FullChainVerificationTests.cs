using EasyCon.Core.Runner;
using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Script.Modules;
using EasyCon.Tests.Support;

namespace EasyCon.Tests.Bytecode;

/// <summary>
/// 全链路打通验证（docs/VmSemanticContract.md §五 验证体系）：
/// 真实例程语料 → 统一编译链路（CompileFile，模块管线）→ ECX 落盘 →
/// 双执行器（EcxInterpreter / C VM 进程）→ 输出与事件全量对拍。
/// C VM 基建（构建/执行/TSV 解析）见 Support/CvmRunner。
/// </summary>
[TestFixture]
public class FullChainVerificationTests
{
    string _workDir = null!;
    string _vmBinary = null!;

    [OneTimeSetUp]
    public void BuildVm()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"EcsFullChain_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);
        var binary = CvmRunner.EnsureBuilt(_workDir);
        if (binary == null)
        {
            Assert.Ignore("无 cc 编译器，跳过 C VM 全链路验证");
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

    (int ExitCode, string Stdout, string Stderr) RunCvm(byte[] ecx, string tag)
        => CvmRunner.Run(_vmBinary, ecx, tag, _workDir);

    /// <summary>统一链路全量对拍：CompileFile → EcxImage → EcxInterpreter / C VM 双执行器。</summary>
    void AssertFullChain(string source, string mainFile, string tag)
        => AssertFullChain(source, mainFile, Path.Combine(_workDir, "src-" + tag), tag);

    void AssertFullChain(string source, string mainFile, string srcDir, string tag)
    {
        // 统一编译链路（模块管线）
        var mainPath = File.Exists(mainFile) ? Path.GetFullPath(mainFile) : WriteTempMain(source, mainFile, srcDir);
        var project = ProjectCompiler.CompileProject(mainPath, new CompileOptions { UseDiskCache = false });
        Assert.That(project.Success, Is.True, $"[{tag}] 统一链路编译失败：" + string.Join("\n", project.Diagnostics));

        // ① EcxInterpreter
        var host = EcsTestHost.CreateRecording();
        Assert.That(EcxInterpreter.Run(project.Image!, host), Is.EqualTo(0), $"[{tag}] 解释器执行失败");

        // ② C VM
        var (exitCode, stdout, stderr) = RunCvm(EcxWriter.Write(project.Image!), tag);

        // ---- 对拍 ----
        // 已知偏差白名单（§6）：TIME 为真实墙钟，stub 宿主的耗时行不比较
        static List<string> WithoutTiming(List<string> lines)
            => lines.Where(l => !l.Contains("耗时")).ToList();
        Assert.That(WithoutTiming(CvmRunner.SplitLines(stdout)), Is.EqualTo(WithoutTiming(host.Lines)), $"[{tag}] C VM vs 解释器输出");
        Assert.That(exitCode, Is.EqualTo(0), $"[{tag}] C VM 退出码");

        // 事件对拍（解释器日志 ↔ C VM TSV）
        var expectedEvents = new[]
        {
            (host.KeyLog, "KEY"), (host.KeyStateLog, "KEYST"),
            (host.StickSetLog, "STICK"), (host.StickClickLog, "STICKC"),
            (host.WaitLog, "WAIT"), (host.AmiiboLog, "AMIIBO"), (host.BeepLog, "BEEP"),
        };
        foreach (var (log, tagName) in expectedEvents)
            Assert.That(CvmRunner.ParseTsvByTag(stderr, tagName), Is.EqualTo(log), $"[{tag}] 事件 {tagName}");
    }

    string WriteTempMain(string source, string fileName, string srcDir)
    {
        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(Path.Combine(srcDir, "lib"));
        var path = Path.Combine(srcDir, fileName);
        File.WriteAllText(path, source);
        return path;
    }

    // ---------- 语料 ----------

    [Test]
    public void FullChain_Guangshu_RealRoutine()
    {
        var path = CorpusAssert.ExamplePath("光速过帧v1.4精准版.txt");
        if (path.Length == 0)
            Assert.Ignore("例程文件不存在");
        var source = File.ReadAllText(path);
        // 真实例程 10000 帧较长，缩短帧数保持验证强度与耗时平衡
        var trimmed = source.Replace("_帧数 = 10000", "_帧数 = 20");
        AssertFullChain(trimmed, "光速过帧.ecs", "guangshu");
    }

    [Test]
    public void FullChain_NQueens_Bitwise()
    {
        var path = CorpusAssert.ExamplePath("nqueens_bitwise.ecs");
        if (path.Length == 0)
            Assert.Ignore("例程文件不存在");
        AssertFullChain(File.ReadAllText(path), "nqueens.ecs", "nqueens");
    }

    [Test]
    public void FullChain_Bdsp_RealRoutine()
    {
        var path = CorpusAssert.ExamplePath("BDSP图鉴过帧v2.1光速过帧版.txt");
        if (path.Length == 0)
            Assert.Ignore("例程文件不存在");
        var source = File.ReadAllText(path);
        // 真实例程翻页次数缩减，保持验证强度与耗时平衡
        var trimmed = source.Replace("_翻几次 = 1", "_翻几次 = 2");
        AssertFullChain(trimmed, "bdsp.ecs", "bdsp");
    }

    [Test]
    public void FullChain_ModuleProject_Nested()
    {
        // 独立编译专属链路：嵌套依赖 + 顶层常量 + init 顺序 + 导入标记
        var dir = Path.Combine(_workDir, "proj");
        Directory.CreateDirectory(Path.Combine(dir, "lib", "lib"));
        File.WriteAllText(Path.Combine(dir, "lib", "lib", "utils.ecs"),
            "_scale = 3\nFUNC scale($x:INT):INT\n    RETURN $x * _scale\nENDFUNC\n");
        File.WriteAllText(Path.Combine(dir, "lib", "level.ecs"),
            "IMPORT \"utils.ecs\"\nFUNC level($x:INT):INT\n    RETURN scale($x) + 1\nENDFUNC\n");
        File.WriteAllText(Path.Combine(dir, "main.ecs"),
            "IMPORT \"level.ecs\"\n$r = level(4)\nPRINT $r\n");

        var project = ProjectCompiler.CompileProject(Path.Combine(dir, "main.ecs"));
        Assert.That(project.Success, Is.True, string.Join("\n", project.Diagnostics));

        var moduleHost = EcsTestHost.CreateRecording();
        Assert.That(EcxInterpreter.Run(project.Image!, moduleHost), Is.EqualTo(0));
        Assert.That(moduleHost.Lines, Is.EqualTo(new[] { "13" }));

        var (exitCode, stdout, _) = RunCvm(EcxWriter.Write(project.Image!), "proj");
        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(CvmRunner.SplitLines(stdout), Is.EqualTo(new[] { "13" }));
    }

    [Test]
    public void FullChain_ModuleProject_NestedStructCrossModule()
    {
        // 跨模块嵌套结构体：geo 模块定义 Box(嵌套 Item) 并经函数边界传递。
        // Box 字母序在前 → 嵌套 sid 非零，锁定 .ecx ext 槽契约（旧实现 C VM 加载即失败）
        var dir = Path.Combine(_workDir, "proj-nested");
        Directory.CreateDirectory(Path.Combine(dir, "lib"));
        File.WriteAllText(Path.Combine(dir, "lib", "geo.ecs"), """
            STRUCT Item
                $v:INT
            END
            STRUCT Box
                $it:Item
                $n:INT
            END
            FUNC make():Box
                $b = Box{}
                $b.it.v = 41
                $b.n = 9
                RETURN $b
            ENDFUNC
            FUNC getv($b:Box):INT
                RETURN $b.it.v
            ENDFUNC
            """);
        File.WriteAllText(Path.Combine(dir, "main.ecs"),
            "IMPORT \"geo.ecs\"\n$o = make()\n$r = getv($o)\nPRINT $r\n$w = $o.n\nPRINT $w\n");

        var project = ProjectCompiler.CompileProject(Path.Combine(dir, "main.ecs"),
            new CompileOptions { UseDiskCache = false });
        Assert.That(project.Success, Is.True, string.Join("\n", project.Diagnostics));

        var host = EcsTestHost.CreateRecording();
        Assert.That(EcxInterpreter.Run(project.Image!, host), Is.EqualTo(0));
        Assert.That(host.Lines, Is.EqualTo(new[] { "41", "9" }));

        var (exitCode, stdout, _) = RunCvm(EcxWriter.Write(project.Image!), "proj-nested");
        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(CvmRunner.SplitLines(stdout), Is.EqualTo(new[] { "41", "9" }));
    }

}