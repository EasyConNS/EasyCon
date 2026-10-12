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
/// 真模型 smoke test：YOLO26n 端到端导出（COCO 预训练）× OpenCV DNN 后端。
/// 验证 v1 唯一未定风险：DNN 对 YOLO26 端到端图的兼容性（Load + Forward）+
/// 输出形状确为 [1,300,6] + std YOLO_DETECT 解码协议（命中坐标落在 ROI 内）。
/// 依赖 test/assets/yolo26/yolo26n.onnx（约定见 test/assets/README.md；缺失 → 跳过）。
/// </summary>
[TestFixture]
[Category("Manual")]
[Explicit]   // OpenCV 5 alpha macOS 原生间歇崩溃——点名 filter 跑
public class NetYoloRealModelTests
{
    [Test]
    public void Yolo26n_DnnBackend_EndToEndDetect()
    {
        var assets = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "test", "assets"));
        var modelPath = Path.Combine(assets, "yolo26", "yolo26n.onnx");
        if (!File.Exists(modelPath))
            Assert.Ignore($"缺真机资产（test/assets/README.md）：{modelPath}");

        // 合成 1920×1080 帧（彩色矩形块，COCO 语义无所谓——本测验证管线与形状，非识别质量）
        using var frame = new Mat(1080, 1920, MatType.CV_8UC3, new Scalar(40, 40, 40));
        Cv2.Rectangle(frame, new Rect(200, 150, 300, 220), new Scalar(60, 160, 240), -1);
        Cv2.Rectangle(frame, new Rect(1100, 500, 260, 380), new Scalar(200, 120, 40), -1);
        Cv2.Rectangle(frame, new Rect(700, 700, 180, 180), new Scalar(40, 200, 60), -1);

        var appDir = Path.Combine(Path.GetTempPath(), $"easycon-yolo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(appDir, "models", "yolo26"));
        var link = Path.Combine(appDir, "models", "yolo26", "yolo26n.onnx");
        try
        {
            if (!File.Exists(link)) File.CreateSymbolicLink(link, modelPath);
        }
        catch (UnauthorizedAccessException e)
        {
            Assert.Ignore($"无法创建 symlink（Windows 需开发者模式/管理员权限）：{e.Message}");
        }

        // ROI (100,80) 800×600 → letterbox 640×640：scale = 0.8，newW=640，newH=480，padY=80
        const string source = """
            $t0 = TIME()
            $n = NET_LOAD(__APP__ + "/models/yolo26/yolo26n.onnx")
            PRINT $n
            $c = YOLO_DETECT($n, 100, 80, 800, 600, 0.25)
            PRINT $c
            $r = NET_ROWS()
            PRINT $r
            $k = YOLO_COUNT()
            PRINT $k
            # 命中坐标协议自检：cls >= 0、score ≥ 阈值、坐标落在 ROI（含取整边界）
            $ok = 1
            $i = 0
            WHILE $i < YOLO_COUNT()
                IF YOLO_CLS($i) < 0
                    $ok = 0
                ENDIF
                IF YOLO_SCORE($i) < 0.25
                    $ok = 0
                ENDIF
                IF YOLO_X1($i) < 100 OR YOLO_X2($i) > 900 OR YOLO_Y1($i) < 80 OR YOLO_Y2($i) > 680
                    $ok = 0
                ENDIF
                $i = $i + 1
            END
            PRINT $ok
            $t1 = TIME()
            $ms = $t1 - $t0
            PRINT $ms
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
            Environment = new HostEnvironment([], appDir),
            Inference = inference,
            Capture = new DelegateCaptureSource(FrameDelegateFactory.CreateFrame(() => frame.Clone())),
        };
        session.Run(new CancellationTokenSource().Token, caps);

        string[] lines = io.Lines.ToArray();
        TestContext.Out.WriteLine("脚本输出：" + string.Join(" | ", lines));

        Assert.That(int.Parse(lines[0]), Is.GreaterThanOrEqualTo(0),
            "NET_LOAD：OpenCV DNN 必须能解析 YOLO26 端到端图（风险点 1）");
        int decode = int.Parse(lines[1]);
        Assert.That(decode, Is.GreaterThanOrEqualTo(0),
            "YOLO_DETECT：Forward 必须成功且形状为 [·,·,6]（风险点 2；-1 = 非端到端或推理失败）");
        Assert.That(lines[2], Is.EqualTo("300"),
            "NET_ROWS：端到端导出应输出 [1,300,6]");
        Assert.That(lines[3], Is.EqualTo(decode.ToString()), "YOLO_COUNT 与解码返回一致");
        Assert.That(lines[4], Is.EqualTo("1"), "命中协议自检（类别/置信度/ROI 坐标界）");

        TestContext.Out.WriteLine($"detect+decode 总耗时（含加载）：{lines.LastOrDefault()} ms，命中 {decode} 个");
    }

    /// <summary>
    /// 真实检出：Ultralytics 经典 bus.jpg（810×1080 街景，COCO 必中巴士 + 多人）。
    /// 验证解码协议吃真数据：命中数 &gt;= 2、存在高置信巴士框（class_id=5）、
    /// 竖图 letterbox（scale≈0.593，padX=80）逆映射坐标落在图内且几何合理。
    /// </summary>
    [Test]
    public void Yolo26n_BusScene_RealDetections_ReverseMapped()
    {
        var assets = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "test", "assets"));
        var modelPath = Path.Combine(assets, "yolo26", "yolo26n.onnx");
        var pngPath = Path.Combine(assets, "yolo26", "bus.jpg");
        if (!File.Exists(modelPath) || !File.Exists(pngPath))
            Assert.Ignore($"缺真机资产（test/assets/README.md）：{modelPath} / {pngPath}");

        using var frame = Cv2.ImRead(pngPath, ImreadModes.Color);
        Assert.That(frame.Empty(), Is.False, "bus.jpg 解码失败");
        Assert.That((frame.Width, frame.Height), Is.EqualTo((810, 1080)), "布局基准");

        var appDir = Path.Combine(Path.GetTempPath(), $"easycon-yolo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(appDir, "models", "yolo26"));
        var link = Path.Combine(appDir, "models", "yolo26", "yolo26n.onnx");
        try
        {
            if (!File.Exists(link)) File.CreateSymbolicLink(link, modelPath);
        }
        catch (UnauthorizedAccessException e)
        {
            Assert.Ignore($"无法创建 symlink（Windows 需开发者模式/管理员权限）：{e.Message}");
        }

        // 每命中打 6 行：cls / score% / x1 / y1 / x2 / y2（屏幕坐标，逆映射后）
        const string source = """
            $n = NET_LOAD(__APP__ + "/models/yolo26/yolo26n.onnx")
            PRINT $n
            $c = YOLO_DETECT($n, 0, 0, 810, 1080, 0.25)
            PRINT $c
            $i = 0
            WHILE $i < YOLO_COUNT()
                $cls = YOLO_CLS($i)
                PRINT $cls
                $pct = INT(YOLO_SCORE($i) * 100.00 + 0.5)
                PRINT $pct
                $x1 = YOLO_X1($i)
                PRINT $x1
                $y1 = YOLO_Y1($i)
                PRINT $y1
                $x2 = YOLO_X2($i)
                PRINT $x2
                $y2 = YOLO_Y2($i)
                PRINT $y2
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
            Environment = new HostEnvironment([], appDir),
            Inference = inference,
            Capture = new DelegateCaptureSource(FrameDelegateFactory.CreateFrame(() => frame.Clone())),
        };
        session.Run(new CancellationTokenSource().Token, caps);

        string[] lines = io.Lines.ToArray();
        Assert.That(int.Parse(lines[0]), Is.GreaterThanOrEqualTo(0), "模型加载");
        int count = int.Parse(lines[1]);
        TestContext.Out.WriteLine($"命中 {count} 个：");
        var hits = new List<(int Cls, int Pct, int X1, int Y1, int X2, int Y2)>();
        for (int i = 0; i < count; i++)
        {
            var hit = (
                Cls: int.Parse(lines[2 + i * 6]),
                Pct: int.Parse(lines[3 + i * 6]),
                X1: int.Parse(lines[4 + i * 6]),
                Y1: int.Parse(lines[5 + i * 6]),
                X2: int.Parse(lines[6 + i * 6]),
                Y2: int.Parse(lines[7 + i * 6]));
            hits.Add(hit);
            TestContext.Out.WriteLine($"  cls={hit.Cls} score={hit.Pct}% box=({hit.X1},{hit.Y1})-({hit.X2},{hit.Y2})");
        }

        Assert.Multiple(() =>
        {
            Assert.That(count, Is.GreaterThanOrEqualTo(2), "bus.jpg 街景应检出巴士 + 人（≥2 命中）");
            Assert.That(hits.Any(h => h.Cls == 5 && h.Pct >= 50), Is.True, "存在高置信巴士（COCO class_id=5）");
            // 逆映射几何：全部命中在图内、框正、巴士框足够宽（横贯下半幅）
            Assert.That(hits.All(h => h.X1 >= 0 && h.Y1 >= 0 && h.X2 <= 810 && h.Y2 <= 1080 && h.X1 < h.X2 && h.Y1 < h.Y2),
                Is.True, "坐标逆映射应落在 ROI 图内且框几何有效");
            Assert.That(hits.Where(h => h.Cls == 5).Max(h => h.X2 - h.X1), Is.GreaterThan(200), "巴士框宽度");
        });
    }
}
