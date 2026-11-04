using EasyCon.Core.Capabilities;
using EasyCon.Core.Runner;
using EasyCon.Core.Script;
using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Tests.Support;
using EasyScript;
using System.Collections.Immutable;

namespace EasyCon.Tests.Capabilities;

/// <summary>
/// ONNX 推理脚本面：NET_LOAD/NET_SCALE/NET_RUNH 走 L3 名表转发 IInference（stub 演示脚本），
/// 镜像 Vision 特征位扫描，以及 C VM 对含 Vision 位镜像的拒跑（exit=ECS_ERR_FEAT）。
/// </summary>
[TestFixture]
public class VisionInferenceTests
{
    const string Script = """
        $net = NET_LOAD("/models/demo.onnx")
        PRINT $net
        $t = NET_SCALE($net, 1.0, 0.0)
        PRINT $t
        $n = NET_RUNH($net, $t)
        PRINT $n
        $o0 = NET_OUT(0)
        PRINT $o0
        """;

    sealed class StubInference : IInference
    {
        public List<string> Loaded = new();
        public List<(int Handle, float Scale, float Offset)> Transformed = new();

        public int Load(string modelPath)
        {
            Loaded.Add(modelPath);
            return 7;
        }

        public void Unload(int session) { }

        public int[]? LastOutputShape => [1, 1, 2];
        public int HoldTensor(float[] data, int[] shape) => -1;

        public void FreeTensor(int handle) { }

        public float[]? RunHeld(int session, int tensorHandle) => session == 7 ? [0.5f, -1.25f] : null;

        public int TransformTensor(int handle, float scale, float offset)
        {
            Transformed.Add((handle, scale, offset));
            return 10;
        }

        public int Normalize(int handle, float[] mean, float[] std) => -1;

        public void Dispose() { }
    }

    [Test]
    public void NetFunctions_ForwardToIInference()
    {
        var inference = new StubInference();
        var engine = new EasyScriptEngine();
        var session = engine.FromSource(Script,
            new ScriptHostOptions { Compile = new CompileOptions { ExtVars = ImmutableHashSet<string>.Empty, UseDiskCache = false } });

        var io = new RecordingIo();
        var caps = new CapabilitySet
        {
            Console = new ConsoleIoAdapter(io),
            Inference = inference,
        };
        session.Run(new CancellationTokenSource().Token, caps);

        // NET_LOAD → 会话句柄；NET_SCALE → 张量句柄；NET_RUNH → 输出长度；NET_OUT(0) → 元素（纯标量协议）
        Assert.That(inference.Loaded, Is.EqualTo(new[] { "/models/demo.onnx" }));
        Assert.That(inference.Transformed.Count, Is.EqualTo(1));
        Assert.That(inference.Transformed[0].Scale, Is.EqualTo(1.0f));
        Assert.That(io.Lines, Is.EqualTo(new[] { "7", "10", "2", "0.5" }));
    }

    [Test]
    public void NetFunctions_WithoutInference_AreUnavailable()
    {
        var engine = new EasyScriptEngine();
        var session = engine.FromSource(Script,
            new ScriptHostOptions { Compile = new CompileOptions { ExtVars = ImmutableHashSet<string>.Empty, UseDiskCache = false } });
        var io = new RecordingIo();
        var caps = new CapabilitySet { Console = new ConsoleIoAdapter(io) };
        session.Run(new CancellationTokenSource().Token, caps);

        // 无推理能力：NET_LOAD → -1，NET_SCALE/NET_RUNH → 0（缺省值表），NET_OUT → 0
        Assert.That(io.Lines, Is.EqualTo(new[] { "-1", "0", "0", "0" }));
    }

    [Test]
    public void VisionFeatureBit_IsSetForNetImages_Only()
    {
        var netResult = Compilation.CompileSource(Script,
            new CompileOptions { ExtVars = ImmutableHashSet<string>.Empty, UseDiskCache = false });
        Assert.That(netResult.Diagnostics.Where(d => d.IsError).ToList(), Is.Empty, "编译失败");
        Assert.That(netResult.Image!.Features & EcsImageFeatures.Vision, Is.Not.Zero, "NET_* → VISION");

        var plain = Compilation.CompileSource("PRINT \"hi\"",
            new CompileOptions { UseDiskCache = false });
        Assert.That(plain.Image!.Features & EcsImageFeatures.Vision, Is.Zero, "普通脚本无 VISION 位");
    }

    [Test]
    public void Cvm_VisionImage_RefusesWithoutHost_DesktopDegradesOnly()
    {
        // 新契约（docs/VM2.md §6.3，能力矩阵 NET 族切片）：
        // ① C# 缺能力宿主（Native=null → EcsCapabilityDefaults 桌面降级）照常执行；
        // ② C VM native=NULL 无名表降级（MCU 不支持桌面原生）→ 运行期响亮 ECS_ERR_NOSUCHNATIVE(10)；
        // ③ C VM --strict-caps → 加载期 ECS_ERR_FEAT(14) 拒跑。
        var result = Compilation.CompileSource(Script,
            new CompileOptions { ExtVars = ImmutableHashSet<string>.Empty, UseDiskCache = false });
        Assert.That(result.Diagnostics.Where(d => d.IsError).ToList(), Is.Empty, "编译失败");

        var workDir = Path.Combine(Path.GetTempPath(), $"EcsVision_{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        try
        {
            var vmBinary = CvmRunner.EnsureBuilt(workDir);
            if (vmBinary == null)
            {
                Assert.Ignore("无 cc 编译器，跳过 C VM Vision 对拍");
                return;
            }

            // ① C# 桌面降级：参考 syscall + Native=null → 缺省值表（录制型宿主收 PRINT 行）
            var host = EcsTestHost.CreateRecording();
            Assert.That(EcxInterpreter.Run(result.Image!, host), Is.EqualTo(0));
            Assert.That(host.Lines, Is.EqualTo(new[] { "-1", "0", "0", "0" }), "桌面缺省值表降级");

            byte[] ecx = EcsContainer.WriteImage(result.Image!);
            // ② C VM：无名表降级 → 响亮失败
            var (exitCode, stdout, stderr) = CvmRunner.Run(vmBinary, ecx, "vision-stub", workDir);
            Assert.That(exitCode, Is.EqualTo(10), $"C VM native 缺失应响亮失败；stderr={stderr}");
            Assert.That(CvmRunner.SplitLines(stdout), Is.Empty, "首条语句即 NET_LOAD，无输出");

            // ③ strict
            var (strictExit, _, strictErr) = CvmRunner.Run(vmBinary, ecx, "vision-strict", workDir,
                extraArgs: "--strict-caps");
            const int EcsErrFeat = 14;
            Assert.That(strictExit, Is.EqualTo(EcsErrFeat),
                $"strict_caps 应拒跑含 Vision 位的镜像（ECS_ERR_FEAT）：{strictErr}");
        }
        finally
        {
            try { Directory.Delete(workDir, true); }
            catch { }
        }
    }
}