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
/// P5 ONNX 推理脚本面：NET_LOAD/NET_RUN 走 L3 名表转发 IInference（stub 演示脚本），
/// 镜像 Vision 特征位扫描，以及 C VM 对含 Vision 位镜像的拒跑（exit=ECS_ERR_FEAT）。
/// </summary>
[TestFixture]
public class VisionInferenceTests
{
    const string Script = """
        $net = NET_LOAD("/models/demo.onnx")
        PRINT $net
        $in = [1, 2, 3]
        $n = NET_RUN($net, $in)
        PRINT $n
        $o0 = NET_OUT(0)
        PRINT $o0
        """;

    sealed class StubInference : IInference
    {
        public List<string> Loaded = new();
        public List<float[]> Ran = new();

        public int Load(string modelPath)
        {
            Loaded.Add(modelPath);
            return 7;
        }

        public float[]? Run(int session, float[] input)
        {
            Ran.Add(input);
            return session == 7 ? [0.5f, -1.25f] : null;
        }

        public void Unload(int session) { }

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

        // NET_LOAD → 会话句柄；NET_RUN → 输出长度；NET_OUT(0) → 元素（纯标量协议）
        Assert.That(inference.Loaded, Is.EqualTo(new[] { "/models/demo.onnx" }));
        Assert.That(inference.Ran.Count, Is.EqualTo(1));
        Assert.That(inference.Ran[0], Is.EqualTo(new[] { 1f, 2f, 3f }));
        Assert.That(io.Lines, Is.EqualTo(new[] { "7", "2", "0.5" }));
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

        // 无推理能力：NET_LOAD → -1，NET_RUN → 0（长度），NET_OUT → 0
        Assert.That(io.Lines, Is.EqualTo(new[] { "-1", "0", "0" }));
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
    public void Cvm_RejectsVisionBitImage_WithFeatError()
    {
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
                Assert.Ignore("无 cc 编译器，跳过 C VM Vision 拒跑验证");
                return;
            }

            byte[] ecx = EcxWriter.Write(result.Image!);
            var (exitCode, _, stderr) = CvmRunner.Run(vmBinary, ecx, "vision-feat", workDir);
            // ECS_ERR_FEAT = 14（ecs_vm.h；C 侧加载器通用校验，宿主 feats 不含 VISION 即拒跑）
            const int EcsErrFeat = 14;
            Assert.That(exitCode, Is.EqualTo(EcsErrFeat),
                "C VM 应拒跑含 Vision 位的镜像（ECS_ERR_FEAT）：" + stderr);
        }
        finally
        {
            try { Directory.Delete(workDir, true); }
            catch { }
        }
    }
}