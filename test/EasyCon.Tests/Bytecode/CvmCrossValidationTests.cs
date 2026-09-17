using EasyCon.Core.Runner;
using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Script.Modules;
using EasyCon.Script.Syntax;
using EasyCon.Script.Text;
using EasyCon.Tests.Support;
using System.Collections.Immutable;

namespace EasyCon.Tests.Bytecode;

/// <summary>
/// 双方交叉验证（docs/VmSemanticContract.md §五）：统一编译链路产出的 .ecx 由
/// 模拟解释器 EcxInterpreter ↔ C 虚拟机（ecs-vm）独立执行，输出与事件全量对拍。
/// C 进程经 cc 现场编译（§5 V7：无 cc 时跳过）；事件 TSV 格式与 EcxHost.EnableRecording 一致。
/// 常规语义语料已文件化至 corpus/（CorpusCrossValidationTests 数据驱动三方对拍）；
/// 本文件保留结构断言（模块管线镜像、错误码传播、调用深度上限、NeedIL 拒绝）。
/// C VM 基建（构建/执行/TSV 解析）见 Support/CvmRunner。
/// </summary>
[TestFixture]
public class CvmCrossValidationTests
{
    string _workDir = null!;
    string _vmBinary = null!;

    [OneTimeSetUp]
    public void BuildVm()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"EcsCvm_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);
        var binary = CvmRunner.EnsureBuilt(_workDir);
        if (binary == null)
        {
            Assert.Ignore("无 cc 编译器，跳过 C VM 交叉验证（§5 V7：CI 无 cc 时跳过）");
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

    (EcxImage Image, List<string> Lines, List<List<string>> EventLogs) RunInterpreter(EcxImage image)
    {
        var host = EcsTestHost.CreateRecording();
        var code = EcxInterpreter.Run(image, host);
        Assert.That(code, Is.EqualTo(0), $"模拟解释器执行失败：{code}");
        return (image, host.Lines, EcsTestHost.EventLogGroups(host));
    }

    (int ExitCode, string Stdout, string Stderr) RunCvm(byte[] ecx, string tag)
        => CvmRunner.Run(_vmBinary, ecx, tag, _workDir);

    // ---------- 结构断言（模块管线镜像 / 错误码 / 深度上限 / NeedIL；常规语义见 corpus/） ----------

    [Test]
    public void TwoWay_ModulePipelineImage()
    {
        // 独立编译管线产出的镜像（含 <init>/<main> 合成 + 导入标记链接）同批验证
        WriteLib("lib/lib/utils.ecs", """
            PRINT "init-utils"
            FUNC twice($x):INT
                RETURN $x * 2
            ENDFUNC
            """);
        WriteLib("lib/mathx.ecs", """
            IMPORT "utils.ecs"
            FUNC triple($x:INT):INT
                RETURN twice($x) + $x
            ENDFUNC
            """);
        var mainPath = Path.Combine(_dir(), "main.ecs");
        File.WriteAllText(mainPath, """
            IMPORT "mathx.ecs"
            $r = triple(5)
            PRINT $r
            """);

        var project = ProjectCompiler.CompileProject(mainPath, new CompileOptions { UseDiskCache = false });
        Assert.That(project.Success, Is.True, string.Join("\n", project.Diagnostics));
        var (_, lines, eventLogs) = RunInterpreter(project.Image!);
        var ecx = EcxWriter.Write(project.Image!);
        var (exitCode, stdout, stderr) = RunCvm(ecx, "modules");

        Assert.That(exitCode, Is.EqualTo(0), $"C VM 退出码：{exitCode}；stderr={stderr}");
        Assert.That(CvmRunner.SplitLines(stdout), Is.EqualTo(lines));
        Assert.That(CvmRunner.SplitLines(stdout), Is.EqualTo(new[] { "init-utils", "15" }));
        Assert.That(CvmRunner.ParseEventLogs(stderr), Is.EqualTo(eventLogs));
    }

    [Test]
    public void TwoWay_ErrorPropagation_DivZero()
    {
        var result = Compilation.CompileSource("$x = 1 / 0\nPRINT $x\n",
            new CompileOptions { UseDiskCache = false });
        var image = result.Image!;

        // 模拟解释器：ECS_ERR_DIVZERO(8)
        var host = new EcxHost();
        int code = EcxInterpreter.Run(image, host);
        var (exitCode, _, _) = RunCvm(EcxWriter.Write(image), "divzero");
        Assert.That(code, Is.EqualTo(EcxInterpreter.ERR_DIVZERO), "模拟解释器应报除零");
        Assert.That(exitCode, Is.EqualTo(EcxInterpreter.ERR_DIVZERO), "C VM 应报除零");
    }

    [Test]
    public void Cvm_CallDepthLimit()
    {
        // V6 协议：失控递归被 ECS_MAX_CALL_DEPTH（512）拦截 → ECS_ERR_DEPTH(9)；
        // 须用非尾递归（尾递归已被 SsaTailRecursionElimination 转为循环，不占调用栈）
        // C# 解释器帧为堆分配不设限（与金标准一致），深度保护是单片机侧职责
        const string source = """
        FUNC rec($n):INT
            $y = rec($n + 1)
            RETURN $y
        ENDFUNC
        $z = rec(0)
        PRINT $z
        """;
        var result = Compilation.CompileSource(source, new CompileOptions { UseDiskCache = false });
        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty,
            string.Join("; ", result.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
        var image = result.Image!;
        var (exitCode, _, stderr) = RunCvm(EcxWriter.Write(image), "deep");
        Assert.That(exitCode, Is.EqualTo(9), $"失控递归应报 ECS_ERR_DEPTH(9)；stderr={stderr}");
    }

    [Test]
    public void Mcu_ImageLabel_Rejected()
    {
        // 单片机约束：携带图像标签的镜像（NeedIL）→ 加载期 ECS_ERR_IL 拒绝执行
        var mainPath = Path.Combine(_dir(), "main.ecs");
        File.WriteAllText(mainPath, "PRINT @enemy\n");

        // @enemy 需在 extVars 白名单内才会通过绑定
        var result = Compilation.CompileFile(mainPath, new CompileOptions
        {
            ExtVars = ImmutableHashSet.Create("enemy"),
            UseDiskCache = false,
        });
        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty,
            string.Join("; ", result.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
        var image = result.Image!;
        Assert.That(image.NeedIL, Is.True, "语料应携带图像标签");

        var ecx = EcxWriter.Write(image);
        var (exitCode, _, stderr) = RunCvm(ecx, "il");
        Assert.That(exitCode, Is.EqualTo(13), $"应拒绝执行（ECS_ERR_IL=13）；stderr={stderr}");
    }

    string _dir()
    {
        var d = Path.Combine(_workDir, "proj");
        Directory.CreateDirectory(Path.Combine(d, "lib", "lib"));
        return d;
    }

    void WriteLib(string relativePath, string code)
    {
        var path = Path.Combine(_dir(), relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, code);
    }
}