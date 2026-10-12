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
        NET_FREE $t
        NET_UNLOAD $net
        PRINT "done"
        """;

    /// <summary>句柄协议 stub：会话 5、张量任意；输出 = 输入前两元素（验证数据通路）。</summary>
    sealed class StubInference : IInference
    {
        public float[]? LastInput;
        public int Unloaded;
        public int Freed;

        public int Load(string modelPath) => 5;

        public int[]? LastOutputShape => null;
        public int HoldTensor(float[] data, int[] shape) => 9;

        public void FreeTensor(int handle) => Freed++;

        public float[]? RunHeld(int session, int tensorHandle) => session == 5 ? LastInput : null;

        public int TransformTensor(int handle, float scale, float offset)
        {
            LastInput = [scale, offset];
            return 10;
        }

        public int Normalize(int handle, float[] mean, float[] std) => -1;

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
/// 依赖 test/assets/ppocr/ 真机资产（约定见 test/assets/README.md；缺失 → 跳过）。
/// </summary>
[TestFixture]
[Category("Manual")]
[Explicit]   // OpenCV 5 alpha macOS 原生间歇崩溃（DNN 线程退出 objc over-release，见 PpocrDet remarks）——点名 filter 跑
public class NetPpocrRealModelTests
{
    /// <summary>
    /// 宝可梦能力值识别 + 计算（examples/pokemon-stats.ecs）：
    /// rec 模型固定 ROI 解码数字 → 能力值求和。ppocr2.png 英文状态页布局。
    /// </summary>
    [Test]
    public void PokemonStats_RecAndCompute()
    {
        // 真机资产走 test/assets 约定（见 test/assets/README.md）：仓库零硬编码环境路径
        var assets = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "test", "assets"));
        var modelPath = Path.Combine(assets, "ppocr", "PP-OCRv5_mobile_rec.onnx");
        var pngPath = Path.Combine(assets, "ppocr", "ppocr2.png");
        if (!File.Exists(modelPath) || !File.Exists(pngPath))
            Assert.Ignore($"缺真机资产（test/assets/README.md）：{modelPath} / {pngPath}");

        using var frame = Cv2.ImRead(pngPath, ImreadModes.Color);
        Assert.That(frame.Empty(), Is.False, "截图解码失败");

        var appDir = Path.Combine(Path.GetTempPath(), $"easycon-stats-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(appDir, "models", "ppocr"));
        var link = Path.Combine(appDir, "models", "ppocr", "PP-OCRv5_mobile_rec.onnx");
        var dictLink = Path.Combine(appDir, "models", "ppocr", "ppocr_keys_v5.txt");
        try
        {
            if (!File.Exists(link)) File.CreateSymbolicLink(link, modelPath);
            if (!File.Exists(dictLink)) File.CreateSymbolicLink(dictLink, Path.Combine(assets, "ppocr", "ppocr_keys_v5.txt"));
        }
        catch (UnauthorizedAccessException e)
        {
            Assert.Ignore($"无法创建 symlink（Windows 需开发者模式/管理员权限）：{e.Message}");
        }

        var source = File.ReadAllText(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "examples", "pokemon-stats.ecs"));

        var engine = new EasyScriptEngine();
        var session = engine.FromSource(source,
            new ScriptHostOptions { Compile = new CompileOptions { ExtVars = ImmutableHashSet<string>.Empty, UseDiskCache = false } });
        var io = new RecordingIo();
        using var inference = new DnnInference();
        var caps = new CapabilitySet
        {
            Console = new ConsoleIoAdapter(io),
            Environment = new HostEnvironment([], appDir),
            Inference = inference,
            Capture = new DelegateCaptureSource(FrameDelegateFactory.CreateFrame(() => frame.Clone())),
            // __ROI__ 走桌面参考回落（DesktopVisionService），测试零装配——与采集路径统一
        };
        session.Run(new CancellationTokenSource().Token, caps);

        TestContext.Out.WriteLine(string.Join("\n", io.Lines));
        Assert.That(io.Lines, Does.Contain("done"), "脚本应正常跑完");

        // ppocr2.png：ATTACK 31 / DEFENSE 46 / SP.ATK 46 / SP.DEF 32 / SPEED 28
        string? LineAfter(string prefix) =>
            io.Lines.FirstOrDefault(l => l.StartsWith(prefix))?[prefix.Length..];

        Assert.That(LineAfter("NAME: "), Is.EqualTo("EXEGGCUTE"), "名称识别（PP_OCR_TEXT 整字典）");
        Assert.That(LineAfter("ATTACK: "), Is.EqualTo("31"), "ATTACK 解码");
        Assert.That(LineAfter("DEFENSE: "), Is.EqualTo("46"), "DEFENSE 解码");
        Assert.That(LineAfter("SP.ATK: "), Is.EqualTo("46"), "SP.ATK 解码");
        Assert.That(LineAfter("SP.DEF: "), Is.EqualTo("32"), "SP.DEF 解码");
        Assert.That(LineAfter("SPEED: "), Is.EqualTo("28"), "SPEED 解码");
        Assert.That(LineAfter("EXP POINTS: "), Is.EqualTo("19531"), "EXP 解码");
        Assert.That(LineAfter("STAT SUM: "), Is.EqualTo("183"), "31+46+46+32+28 = 183");
    }

    /// <summary>
    /// 真实字典 × 标签表层交叉验证（test/assets/ppocr/ppocr_keys_v5.txt）：
    /// ① NET_LABELS 加载官方字典（18383 行）；② CTC class → NET_LABEL(cls-1) 对齐
    /// （blank=0 占位，std 数字常数 16163-16172 的字典行号实证）；
    /// ③ 同一 EXP 行真模型输出上，脚本侧自实现折叠（NET_ARGMAX × NET_LABEL，策略层
    /// 可替换性的实机演示）与宿主 CTC_FOLD 洞结果一致（"19531"）。
    /// </summary>
    [Test]
    public void PpocrDictionary_Labels_CrossValidate_ScriptSideFold()
    {
        var assets = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "test", "assets"));
        var modelPath = Path.Combine(assets, "ppocr", "PP-OCRv5_mobile_rec.onnx");
        var pngPath = Path.Combine(assets, "ppocr", "ppocr2.png");
        var dictPath = Path.Combine(assets, "ppocr", "ppocr_keys_v5.txt");
        if (!File.Exists(modelPath) || !File.Exists(pngPath) || !File.Exists(dictPath))
            Assert.Ignore($"缺真机资产（test/assets/README.md）：{modelPath} / {pngPath} / {dictPath}");

        using var frame = Cv2.ImRead(pngPath, ImreadModes.Color);
        Assert.That(frame.Empty(), Is.False, "截图解码失败");

        var appDir = Path.Combine(Path.GetTempPath(), $"easycon-dict-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(appDir, "models", "ppocr"));
        var link = Path.Combine(appDir, "models", "ppocr", "PP-OCRv5_mobile_rec.onnx");
        try
        {
            if (!File.Exists(link)) File.CreateSymbolicLink(link, modelPath);
        }
        catch (UnauthorizedAccessException e)
        {
            Assert.Ignore($"无法创建 symlink（Windows 需开发者模式/管理员权限）：{e.Message}");
        }

        // PP_OCR_DIGITS 跑洞折叠后，_lastNetOutput 仍驻留 EXP 行 [1,T,C] 输出——
        // 脚本侧 NET_ROWS + NET_ARGMAX + NET_LABEL(cls-1) 对同一输出自实现同语义折叠
        // （blank 重置、连续去重、数字窗口 16163-16172 过滤，与洞逐条对应）。
        // 字典路径经 ARG(0) 注入（路径含空格，不内插进脚本字面量）。
        const string source = """
            $n = NET_LOAD(__APP__ + "/models/ppocr/PP-OCRv5_mobile_rec.onnx")
            $dict = NET_LABELS(ARG(0))
            PRINT $dict
            $c0 = NET_LABEL(16162)
            PRINT $c0
            $c5 = NET_LABEL(16167)
            PRINT $c5
            $hole = PP_OCR_DIGITS($n, 355, 680, 1370, 85)
            PRINT $hole
            $out = ""
            $prev = -1
            $i = 0
            WHILE $i < NET_ROWS()
                $cls = NET_ARGMAX($i)
                IF $cls == 0
                    $prev = -1
                ELSE
                    IF $cls != $prev
                        IF $cls >= 16163
                            IF $cls <= 16172
                                $out = $out + NET_LABEL($cls - 1)
                            ENDIF
                        ENDIF
                    ENDIF
                    $prev = $cls
                ENDIF
                $i = $i + 1
            END
            PRINT $out
            """;

        var engine = new EasyScriptEngine();
        var session = engine.FromSource(source,
            new ScriptHostOptions
            {
                Compile = new CompileOptions { ExtVars = ImmutableHashSet<string>.Empty, UseDiskCache = false },
            });
        Assert.That(session.Info.Diagnostics.Where(d => d.IsError), Is.Empty, "脚本必须编译通过");

        var io = new RecordingIo();
        using var inference = new DnnInference();
        var caps = new CapabilitySet
        {
            Console = new ConsoleIoAdapter(io),
            Environment = new HostEnvironment([dictPath], appDir),
            Inference = inference,
            Capture = new DelegateCaptureSource(FrameDelegateFactory.CreateFrame(() => frame.Clone())),
        };
        session.Run(new CancellationTokenSource().Token, caps);

        TestContext.Out.WriteLine(string.Join("\n", io.Lines));
        // 字典行数；class 16163/16168 → '0'/'5'；洞折叠与脚本侧自实现折叠同答 "19531"
        Assert.That(io.Lines, Is.EqualTo(new[] { "18383", "0", "5", "19531", "19531" }));
    }
}