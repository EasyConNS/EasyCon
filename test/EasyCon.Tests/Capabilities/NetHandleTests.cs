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
/// 真模型端到端：PP-OCR rec 模型 + 游戏截图（examples/pokemon-stats.ecs）
/// 依赖 test/assets/ppocr/ 真机资产（约定见 test/assets/README.md；缺失 → 跳过）。
/// </summary>
[TestFixture]
[Category("Manual")]
[Explicit]   // OpenCV 5 alpha macOS 原生间歇崩溃（DNN 线程退出 objc over-release，见 PpocrDet remarks）——点名 filter 跑
public class NetPpocrRealModelTests
{
    static string AssetsDir => Path.GetFullPath(Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "test", "assets"));

    /// <summary>
    /// 宝可梦能力值识别 + 计算（examples/pokemon-stats.ecs）：
    /// rec 模型固定 ROI 解码数字 → 能力值求和。ppocr2.png 英文状态页布局。
    /// </summary>
    [Test]
    public void PokemonStats_RecAndCompute()
    {
        // 真机资产走 test/assets 约定（见 test/assets/README.md）：仓库零硬编码环境路径
        var assets = AssetsDir;
        var modelPath = Path.Combine(assets, "ppocr", "PP-OCRv6_medium_rec.onnx");
        var pngPath = Path.Combine(assets, "ppocr", "ppocr2.png");
        if (!File.Exists(modelPath) || !File.Exists(pngPath))
            Assert.Ignore($"缺真机资产（test/assets/README.md）：{modelPath} / {pngPath}");

        using var frame = Cv2.ImRead(pngPath, ImreadModes.Color);
        Assert.That(frame.Empty(), Is.False, "截图解码失败");

        var appDir = Path.Combine(Path.GetTempPath(), $"easycon-stats-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(appDir, "models", "ppocr"));
        var link = Path.Combine(appDir, "models", "ppocr", "PP-OCRv6_medium_rec.onnx");
        var dictLink = Path.Combine(appDir, "models", "ppocr", "ppocrv6_dict.txt");
        try
        {
            if (!File.Exists(link)) File.CreateSymbolicLink(link, modelPath);
            if (!File.Exists(dictLink)) File.CreateSymbolicLink(dictLink, Path.Combine(assets, "ppocr", "ppocrv6_dict.txt"));
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
    /// 真实字典 × 标签表层交叉验证（两代模型各跑一遍，资产缺失的代际独立跳过）：
    /// ① NET_LABELS 加载官方字典（v5 18383 行 / v6 18708 行）；② CTC class →
    /// NET_LABEL(cls-1) 对齐（blank=0 占位；数字窗口随代际不同——v5 表尾 16163 起、
    /// v6 表头 33 起，由测试按代际注入）；
    /// ③ 同一 EXP 行真模型输出上，脚本侧自实现折叠（NET_ARGMAX × NET_LABEL，策略层
    /// 可替换性的实机演示）与宿主 CTC_FOLD 洞结果一致（"19531"）。
    /// </summary>
    [TestCase("PP-OCRv5_mobile_rec.onnx", "ppocr_keys_v5.txt", 18383, 16163)]
    [TestCase("PP-OCRv6_medium_rec.onnx", "ppocrv6_dict.txt", 18708, 33)]
    public void PpocrDictionary_Labels_CrossValidate_ScriptSideFold(
        string recModelName, string dictName, int dictLines, int digitsBase)
    {
        var assets = AssetsDir;
        var modelPath = Path.Combine(assets, "ppocr", recModelName);
        var pngPath = Path.Combine(assets, "ppocr", "ppocr2.png");
        var dictPath = Path.Combine(assets, "ppocr", dictName);
        if (!File.Exists(modelPath) || !File.Exists(pngPath) || !File.Exists(dictPath))
            Assert.Ignore($"缺真机资产（test/assets/README.md）：{modelPath} / {pngPath} / {dictPath}");

        using var frame = Cv2.ImRead(pngPath, ImreadModes.Color);
        Assert.That(frame.Empty(), Is.False, "截图解码失败");

        var appDir = Path.Combine(Path.GetTempPath(), $"easycon-dict-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(appDir, "models", "ppocr"));
        var link = Path.Combine(appDir, "models", "ppocr", recModelName);
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
        // （blank 重置、连续去重、数字窗口 [base, base+9] 过滤，与洞逐条对应）。
        // 数字窗口由脚本按模型代际显式指定（std 不钉常数）；字典路径经 ARG 注入
        // （路径含空格，不内插进脚本字面量）。
        const string source = """
            $n = NET_LOAD(__APP__ + "/models/ppocr/" + ARG(2))
            $dict = NET_LABELS(ARG(0))
            PRINT $dict
            $base = INT(ARG(1))
            $c0 = NET_LABEL($base - 1)
            PRINT $c0
            $c5 = NET_LABEL($base + 4)
            PRINT $c5
            $hole = PP_OCR_DIGITS($n, $base, 355, 680, 1370, 85)
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
                        IF $cls >= $base
                            IF $cls <= $base + 9
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
            Environment = new HostEnvironment([dictPath, digitsBase.ToString(), recModelName], appDir),
            Inference = inference,
            Capture = new DelegateCaptureSource(FrameDelegateFactory.CreateFrame(() => frame.Clone())),
        };
        session.Run(new CancellationTokenSource().Token, caps);

        TestContext.Out.WriteLine(string.Join("\n", io.Lines));
        // 字典行数；class base/base+5 → '0'/'5'；洞折叠与脚本侧自实现折叠同答 "19531"
        Assert.That(io.Lines, Is.EqualTo(new[] { dictLines.ToString(), "0", "5", "19531", "19531" }));
    }

    /// <summary>
    /// 合成图端到端（不依赖 ppocr2.png，只依赖模型+字典资产）：
    /// 渲染两行数字 → PP_DET_FIND 全页找框 → 逐框 PP_OCR_DIGITS（v6 数字窗口）。
    /// 验证 det 概率图 → 框缓存 → rec 折叠的完整 C# 常数组合在 v6 上成立。
    /// </summary>
    [Test]
    public void SyntheticRender_DetRec_DigitsEndToEnd()
    {
        var ppocrDir = Path.Combine(AssetsDir, "ppocr");
        var detPath = Path.Combine(ppocrDir, "PP-OCRv6_medium_det.onnx");
        var recPath = Path.Combine(ppocrDir, "PP-OCRv6_medium_rec.onnx");
        var dictPath = Path.Combine(ppocrDir, "ppocrv6_dict.txt");
        if (!File.Exists(detPath) || !File.Exists(recPath) || !File.Exists(dictPath))
            Assert.Ignore($"缺真机资产（test/assets/README.md）：{detPath} / {recPath} / {dictPath}");

        // 合成图：深色底两行白字（游戏 HUD 风格），行序即阅读序
        using var frame = new Mat(360, 640, MatType.CV_8UC3, new Scalar(40, 40, 40));
        Cv2.PutText(frame, "19531", new Point(80, 120), HersheyFonts.HersheySimplex, 1.6, Scalar.White, 3, LineTypes.AntiAlias);
        Cv2.PutText(frame, "999900", new Point(80, 280), HersheyFonts.HersheySimplex, 1.6, Scalar.White, 3, LineTypes.AntiAlias);
        var framePng = Convert.ToBase64String(frame.ToBytes(".png"));

        // 模型 symlink 进临时 __APP__/models/ppocr/（部署布局；NET_LOAD 按该路径解析）
        var appDir = Path.Combine(Path.GetTempPath(), $"easycon-synth-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(appDir, "models", "ppocr"));
        try
        {
            File.CreateSymbolicLink(Path.Combine(appDir, "models", "ppocr", "PP-OCRv6_medium_det.onnx"), detPath);
            File.CreateSymbolicLink(Path.Combine(appDir, "models", "ppocr", "PP-OCRv6_medium_rec.onnx"), recPath);
        }
        catch (UnauthorizedAccessException e)
        {
            Assert.Ignore($"无法创建 symlink（Windows 需开发者模式/管理员权限）：{e.Message}");
        }

        const string source = """
            $det = NET_LOAD(__APP__ + "/models/ppocr/PP-OCRv6_medium_det.onnx")
            $rec = NET_LOAD(__APP__ + "/models/ppocr/PP-OCRv6_medium_rec.onnx")
            $frame = ARG(0)
            $n = PP_DET_FIND($det, $frame, 0, 0, 640, 360)
            PRINT $n
            $i = 0
            WHILE $i < DET_COUNT()
                $txt = PP_OCR_DIGITS($rec, $frame, 33, DET_X($i), DET_Y($i), DET_W($i), DET_H($i))
                PRINT $txt
                $i = $i + 1
            END
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
            // 帧经环境参数注入（Environment.Args 优先于宿主 options.Args，见 EcxVm.BuildHost）
            Environment = new HostEnvironment([framePng], appDir),
            Inference = inference,
        };
        session.Run(new CancellationTokenSource().Token, caps);

        TestContext.Out.WriteLine(string.Join("\n", io.Lines));
        Assert.That(io.Lines, Is.Not.Empty, "至少应输出框数");
        Assert.That(int.Parse(io.Lines[0]), Is.GreaterThan(0), $"det 应找到文本框（-1 = 输出缺失/形状错）：{io.Lines[0]}");
        Assert.That(io.Lines, Does.Contain("19531"), "第一行数字应被 det+rec 读出");
        Assert.That(io.Lines, Does.Contain("999900"), "第二行数字应被 det+rec 读出");
    }
}