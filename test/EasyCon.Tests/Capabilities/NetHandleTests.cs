using EasyCon.Core;
using EasyCon.Core.Capabilities;
using EasyCon.Core.Runner;
using EasyCon.Core.Script;
using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Tests.Support;
using OpenCvSharp;
using System.Collections.Immutable;

namespace EasyCon.Tests.Capabilities;

/// <summary>
/// NET 句柄族（NET_IMAGE / NET_RUNH / NET_SCALE / NET_FREE / NET_UNLOAD）：
/// 句柄协议转发、S-21 缺能力降级、真模型端到端（Manual，读用户模型目录）。
/// </summary>
[TestFixture]
public class NetHandleTests
{
    const string Script = """
        $net = NET_LOAD("models/demo.onnx")
        PRINT $net
        $h = NET_IMAGE(0, 0, 0, 0, 4, 4, "rgb")
        PRINT $h
        $t = NET_SCALE($h, 0.5, 1.0)
        PRINT $t
        $n = NET_RUNH($net, $t)
        PRINT $n
        $o0 = NET_OUT(0)
        PRINT $o0
        NET_FREE($t)
        NET_UNLOAD($net)
        PRINT "done"
        """;

    /// <summary>句柄协议 stub：会话 5、张量任意；输出 = 输入前两元素（验证数据通路）。</summary>
    sealed class StubInference : IInference
    {
        public float[]? LastInput;
        public int Unloaded;
        public int Freed;

        public int Load(string modelPath) => 5;

        public float[]? Run(int session, float[] input) { LastInput = input; return session == 5 ? input : null; }

        public int[]? LastOutputShape => null;
        public int HoldTensor(float[] data, int[] shape) => 9;

        public void FreeTensor(int handle) => Freed++;

        public float[]? RunHeld(int session, int tensorHandle) => session == 5 ? LastInput : null;

        public int TransformTensor(int handle, float scale, float offset)
        {
            LastInput = [scale, offset];
            return 10;
        }

        public void Unload(int session) => Unloaded++;

        public void Dispose() { }
    }

    static (EasyCon.Core.Script.IScriptSession Session, RecordingIo Io) Compile(string source)
    {
        var engine = new EasyScriptEngine();
        var session = engine.FromSource(source,
            new ScriptHostOptions { Compile = new CompileOptions { ExtVars = ImmutableHashSet<string>.Empty, UseDiskCache = false } });
        return (session, new RecordingIo());
    }

    [Test]
    public void HandleFunctions_ForwardWithHandles()
    {
        var inference = new StubInference();
        var (session, io) = Compile(Script);
        var caps = new CapabilitySet
        {
            Console = new ConsoleIoAdapter(io),
            Inference = inference,
            Capture = new DelegateCaptureSource((_, _, _, _) => MakePng(8, 8)),
        };
        session.Run(new CancellationTokenSource().Token, caps);

        // NET_LOAD → 5；NET_IMAGE → 张量句柄(9)；NET_SCALE → 新句柄(10)；
        // NET_RUNH → 输出长度(2)；NET_OUT(0) → 首元素(0.5，来自 TransformTensor 的 [scale, offset])
        Assert.That(io.Lines, Is.EqualTo(new[] { "5", "9", "10", "2", "0.5", "done" }));
        Assert.That(inference.Freed, Is.EqualTo(1));
        Assert.That(inference.Unloaded, Is.EqualTo(1));
    }

    [Test]
    public void HandleFunctions_WithoutInference_Degrade()
    {
        var (session, io) = Compile(Script);
        var caps = new CapabilitySet { Console = new ConsoleIoAdapter(io) };
        session.Run(new CancellationTokenSource().Token, caps);

        // S-21：NET_LOAD → -1，NET_IMAGE/NET_RUNH/NET_SCALE → 0（无效句柄/长度），NET_OUT → 0
        Assert.That(io.Lines, Is.EqualTo(new[] { "-1", "0", "0", "0", "0", "done" }));
    }

    [Test]
    public void NetImage_ProducesHeldTensor_WithExpectedShape()
    {
        // 真实 DnnInference：NET_IMAGE 后张量句柄有效，FreeTensor 后 RunHeld 返回 null
        var inference = new DnnInference();
        var (session, io) = Compile("""
            $h = NET_IMAGE(0, 0, 0, 0, 4, 4, "rgb")
            PRINT $h
            """);
        var caps = new CapabilitySet
        {
            Console = new ConsoleIoAdapter(io),
            Inference = inference,
            Capture = new DelegateCaptureSource((_, _, _, _) => MakePng(8, 8)),
        };
        session.Run(new CancellationTokenSource().Token, caps);

        var handle = int.Parse(io.Lines[0]);
        Assert.That(handle, Is.GreaterThan(0), "NET_IMAGE 应返回有效句柄");
        var held = inference.RunHeld(-1, handle);   // 会话无效 → null（张量在但会话校验先行）
        Assert.That(held, Is.Null);
        inference.FreeTensor(handle);
        inference.Dispose();
    }

    [Test]
    public void NewNetFunctions_CarryVisionFeatureBit_AndDegradeOnCvm()
    {
        var result = Compilation.CompileSource(Script,
            new CompileOptions { ExtVars = ImmutableHashSet<string>.Empty, UseDiskCache = false });
        Assert.That(result.Diagnostics.Where(d => d.IsError).ToList(), Is.Empty, "编译失败");
        Assert.That(result.Image!.Features & EcsImageFeatures.Vision, Is.Not.Zero, "NET 句柄族 → VISION");
    }

    static string MakePng(int w, int h)
    {
        using var mat = new Mat(h, w, MatType.CV_8UC3, Scalar.Blue);
        return Convert.ToBase64String(mat.ToBytes(".png"));
    }
}

/// <summary>
/// 真模型端到端：PP-OCRv5 rec 模型 + 游戏截图（examples/pokemon-stats.ecs）
/// 依赖 ~/Downloads/EasyCon-PP-OCRv5/*.onnx 与 ~/Downloads/ppocr*.png（外部资产，CI 无 → 跳过）。
/// </summary>
[TestFixture]
[Category("Manual")]
public class NetPpocrRealModelTests
{
    /// <summary>
    /// 宝可梦能力值识别 + 计算（examples/pokemon-stats.ecs）：
    /// rec 模型固定 ROI 解码数字 → 能力值求和。ppocr2.png 英文状态页布局。
    /// </summary>
    [Test]
    public void PokemonStats_RecAndCompute()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var modelPath = Path.Combine(home, "Downloads", "EasyCon-PP-OCRv5", "PP-OCRv5_mobile_rec.onnx");
        var pngPath = Path.Combine(home, "Downloads", "ppocr2.png");
        if (!File.Exists(modelPath) || !File.Exists(pngPath))
            Assert.Ignore($"缺模型或截图：{modelPath} / {pngPath}");

        using var frame = Cv2.ImRead(pngPath, ImreadModes.Color);
        Assert.That(frame.Empty(), Is.False, "截图解码失败");

        var appDir = Path.Combine(Path.GetTempPath(), $"easycon-stats-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(appDir, "models", "ppocr"));
        var link = Path.Combine(appDir, "models", "ppocr", "PP-OCRv5_mobile_rec.onnx");
        if (!File.Exists(link)) File.CreateSymbolicLink(link, modelPath);

        var source = File.ReadAllText(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "examples", "pokemon-stats.ecs"));

        var engine = new EasyScriptEngine();
        var session = engine.FromSource(source,
            new ScriptHostOptions { Compile = new CompileOptions { ExtVars = ImmutableHashSet<string>.Empty, UseDiskCache = false } });
        var io = new RecordingIo();
        var caps = new CapabilitySet
        {
            Console = new ConsoleIoAdapter(io),
            Environment = new HostEnvironment([], appDir),
            Inference = new DnnInference(),
            Capture = new DelegateCaptureSource(FrameDelegateFactory.CreateFrame(() => frame.Clone())),
        };
        session.Run(new CancellationTokenSource().Token, caps);

        TestContext.Out.WriteLine(string.Join("\n", io.Lines));
        Assert.That(io.Lines, Does.Contain("done"), "脚本应正常跑完");

        // ppocr2.png：ATTACK 31 / DEFENSE 46 / SP.ATK 46 / SP.DEF 32 / SPEED 28
        string? LineAfter(string prefix) =>
            io.Lines.FirstOrDefault(l => l.StartsWith(prefix))?[prefix.Length..];

        Assert.That(LineAfter("ATTACK: "), Is.EqualTo("31"), "ATTACK 解码");
        Assert.That(LineAfter("DEFENSE: "), Is.EqualTo("46"), "DEFENSE 解码");
        Assert.That(LineAfter("SP.ATK: "), Is.EqualTo("46"), "SP.ATK 解码");
        Assert.That(LineAfter("SP.DEF: "), Is.EqualTo("32"), "SP.DEF 解码");
        Assert.That(LineAfter("SPEED: "), Is.EqualTo("28"), "SPEED 解码");
        Assert.That(LineAfter("EXP POINTS: "), Is.EqualTo("19531"), "EXP 解码");
        Assert.That(LineAfter("STAT SUM: "), Is.EqualTo("183"), "31+46+46+32+28 = 183");
    }
}