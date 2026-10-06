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

    (int ExitCode, string Stdout, string Stderr) RunCvm(byte[] ecx, string tag, string? extraArgs = null)
        => CvmRunner.Run(_vmBinary, ecx, tag, _workDir, extraArgs: extraArgs);

    // ---------- 结构断言（模块管线镜像 / 错误码 / 深度上限 / NeedIL；常规语义见 corpus/） ----------

    [Test]
    public void TwoWay_ModulePipelineImage()
    {
        // 独立编译管线产出的镜像（含 <init>/<main> 合成 + 导入标记链接）同批验证
        WriteLib("lib/utils.ecs", """
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
        var ecx = EcsContainer.WriteImage(project.Image!);
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
        var (exitCode, _, _) = RunCvm(EcsContainer.WriteImage(image), "divzero");
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
        var (exitCode, _, stderr) = RunCvm(EcsContainer.WriteImage(image), "deep");
        Assert.That(exitCode, Is.EqualTo(9), $"失控递归应报 ECS_ERR_DEPTH(9)；stderr={stderr}");
    }

    [Test]
    public void Cvm_Validator_KeyV_KeyCodeOperand_NotATargetSlot()
    {
        // KeyV 的 a 是按键码（GamePadKey）而非槽位，b 才是目标槽——合法脚本的键码
        // 可以远大于 nslots，加载期校验若把 a 当槽位检查会拒载正常镜像；
        // b 越界仍须在加载期拒绝（ECS_ERR_SLOT=5，校验失败即退出码）。
        // 用键码 12(RCLICK) 远超本脚本槽数，确保「a 被误当槽位」必然拒载、用例可抓住回归。
        const string source = """
        FOR $i = 1 TO 2
            RCLICK $i
        NEXT
        """;
        var result = Compilation.CompileSource(source, new CompileOptions { UseDiskCache = false });
        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty,
            string.Join("; ", result.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
        var ecx = EcsContainer.WriteImage(result.Image!);

        var (okExit, _, okErr) = RunCvm(ecx, "keyv-legal");
        Assert.That(okExit, Is.EqualTo(0), $"合法 KeyV 镜像应正常加载执行；stderr={okErr}");
        Assert.That(CvmRunner.ParseTsvByTag(okErr, "KEY"), Is.EqualTo(new[] { "KEY 12 1", "KEY 12 2" }));

        // 把 KeyV 字的 b 槽改写成 0xFE（远超本镜像槽数）→ 加载期拒载
        int patched = 0;
        for (int i = 0; i + 3 < ecx.Length; i++)
        {
            if (ecx[i] == (byte)EcsOpcode.KeyV && ecx[i + 2] < 0x10)
            {
                ecx[i + 2] = 0xFE;
                patched++;
            }
        }
        Assert.That(patched, Is.GreaterThanOrEqualTo(1), "镜像中应存在 KeyV 指令字");
        EcsContainer.ResealCrc32(ecx);   // ECX1 全量 CRC 覆盖补丁字节，重封后补丁才能到达槽位校验（CRC 在前）

        var (badExit, _, badErr) = RunCvm(ecx, "keyv-badslot");
        Assert.That(badExit, Is.EqualTo(5), $"b 越界应报 ECS_ERR_SLOT(5)；stderr={badErr}");
        // 加载期校验失败的标志：ECS_ERR=5 且无运行期错误的 func/pc 定位（中文标签受本地代码页影响，不断言）
        Assert.That(badErr, Does.Contain("ECS_ERR=5"));
        Assert.That(badErr, Does.Not.Contain("func="));
    }

    [Test]
    public void Mcu_ImageLabel_DegradesByDefault_RefusesUnderStrictCaps()
    {
        // S-21 双态：缺省宿主 → 图像标签镜像加载执行（@label → 目标槽 ← -1，与 C# 缺省 ImgLabel
        // 一致）；--strict-caps → 恢复加载期 ECS_ERR_IL 拒跑
        var mainPath = Path.Combine(_dir(), "main.ecs");
        File.WriteAllText(mainPath, "$v = @enemy\nPRINT $v\n");

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

        var ecx = EcsContainer.WriteImage(image);

        // C# 缺省宿主：ImgLabel 缺省 → -1（录制型宿主收 PRINT 行；Native=null → 降级路径）
        var host = EcsTestHost.CreateRecording();
        Assert.That(EcxInterpreter.Run(image, host), Is.EqualTo(0));
        Assert.That(host.Lines, Is.EqualTo(new[] { "-1" }));

        var (exitCode, stdout, stderr) = RunCvm(ecx, "il");
        Assert.That(exitCode, Is.EqualTo(0), $"缺省宿主应降级执行；stderr={stderr}");
        Assert.That(CvmRunner.SplitLines(stdout), Is.EqualTo(new[] { "-1" }), "图像标签缺省值双端锁步");

        var (strictExit, _, strictErr) = RunCvm(ecx, "il-strict", extraArgs: "--strict-caps");
        Assert.That(strictExit, Is.EqualTo(13), $"strict_caps 应恢复加载期拒跑（ECS_ERR_IL=13）；stderr={strictErr}");
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