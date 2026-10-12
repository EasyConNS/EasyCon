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
/// YOLO26 端到端适配（v1 检测闭环）：
/// __NET_IMAGE__ "letter"（等比缩放 + 居中 + 114 灰垫；缩放/取整公式与 std YOLO_DETECT
/// 逆映射同式——零漂移由本文件锁定）、__YOLO_DECODE__ 洞（[·,·,6] 形状守卫 + 置信度过滤 +
/// letterbox→屏幕逆映射）、YOLO_* 访问器、std YOLO_DETECT 配方闭环。
/// stub 推理喂预设 [1,300,6] 输出，无需真模型/截图。
/// </summary>
[TestFixture]
public class YoloDetectTests
{
    /// <summary>stub：RunHeld 返回预设输出；HoldTensor/TransformTensor 记录张量供断言。</summary>
    sealed class StubInference : IInference
    {
        public float[] Output = [];
        public int[] Shape = [1, 0, 6];
        public List<(float[] Data, int[] Shape)> Tensors { get; } = [];
        public int Freed;

        public int Load(string modelPath) => 5;
        public int[]? LastOutputShape => Shape;

        public int HoldTensor(float[] data, int[] shape)
        {
            Tensors.Add(((float[])data.Clone(), (int[])shape.Clone()));
            return Tensors.Count;
        }

        public void FreeTensor(int handle) => Freed++;

        public float[]? RunHeld(int session, int tensorHandle) => Output;

        public int TransformTensor(int handle, float scale, float offset)
        {
            var (data, shape) = Tensors[handle - 1];
            return HoldTensor(data.Select(v => v * scale + offset).ToArray(), shape);
        }

        public int Normalize(int handle, float[] mean, float[] std) => -1;

        public void Unload(int session) { }
        public void Dispose() { }
    }

    /// <summary>构造 [1,300,6] 端到端输出：前若干行按 rows 填充（x1,y1,x2,y2,conf,cls），其余全零。</summary>
    static StubInference EndToEnd(params float[][] rows)
    {
        var output = new float[300 * 6];
        for (int i = 0; i < rows.Length; i++)
            Array.Copy(rows[i], 0, output, i * 6, 6);
        return new StubInference { Output = output, Shape = [1, 300, 6] };
    }

    /// <summary>生成 base64 PNG：第 y 行填 rowColors[y % len]（BGR 序）。</summary>
    static string MakeRowPng(int w, params Scalar[] rowColors)
    {
        using var mat = new Mat(rowColors.Length, w, MatType.CV_8UC3);
        for (int y = 0; y < rowColors.Length; y++)
            Cv2.Rectangle(mat, new Rect(0, y, w, 1), rowColors[y], -1);
        return Convert.ToBase64String(mat.ToBytes(".png"));
    }

    static string Run(string source, StubInference inference, string? capture = null)
    {
        var engine = new EasyScriptEngine();
        var session = engine.FromSource(source,
            new ScriptHostOptions
            {
                Compile = new CompileOptions { ExtVars = ImmutableHashSet<string>.Empty, UseDiskCache = false },
            });
        Assert.That(session.Info.Diagnostics.Where(d => d.IsError), Is.Empty, "脚本必须编译通过");

        var io = new RecordingIo();
        var caps = new CapabilitySet
        {
            Console = new ConsoleIoAdapter(io),
            Inference = inference,
        };
        if (capture != null)
            caps.Capture = new DelegateCaptureSource((_, _, _, _) => capture);

        // 清场：命中缓存是宿主静态槽（跨用例共享）。用独立临时 stub 跑一次 1×6 全零输出、
        // 阈值 0.99 的合法解码（stride 校验通过、洞必被调用、零命中）复位缓存——被测 stub
        // 的张量记录零触碰。否则 stride 校验失败的用例（洞未被调用）会读到残留命中。
        var warmupInference = new StubInference { Output = new float[6], Shape = [1, 1, 6] };
        var warmup = engine.FromSource("""
            $n = NET_LOAD("stub")
            $c = YOLO_DETECT($n, 0, 0, 8, 4, 0.99)
            """,
            new ScriptHostOptions
            {
                Compile = new CompileOptions { ExtVars = ImmutableHashSet<string>.Empty, UseDiskCache = false },
            });
        Assert.That(warmup.Info.Diagnostics.Where(d => d.IsError), Is.Empty, "清场脚本必须编译通过");
        warmup.Run(new CancellationTokenSource().Token, new CapabilitySet
        {
            Console = new ConsoleIoAdapter(new RecordingIo()),
            Inference = warmupInference,
            // YOLO_DETECT 采集形态需要 Capture（frame 尺寸任意，stub 无视输入张量）
            Capture = new DelegateCaptureSource((_, _, _, _) =>
                Convert.ToBase64String(new Mat(4, 8, MatType.CV_8UC3, new Scalar(30, 20, 250)).ToBytes(".png"))),
        });

        session.Run(new CancellationTokenSource().Token, caps);
        return string.Join("\n", io.Lines);
    }

    // ---- __NET_IMAGE__ "letter"：几何 ----

    [Test]
    public void Letterbox_ShortSource_PadsVertically()
    {
        // 8×2 → 8×4：scale = min(1, 2) = 1，newH = 2，padY = (4-2)/2 = 1（上下各 1 行 114 垫）。
        // 行序：[pad, row0, row1, pad]；BGR(30,20,250)/(60,50,40) → RGB(250,20,30)/(40,50,60)。
        var image = MakeRowPng(8, new Scalar(30, 20, 250), new Scalar(60, 50, 40));
        var inference = new StubInference();
        var lines = Run($$"""
            $img = NET_IMAGE("{{image}}", 8, 4, "letter")
            PRINT $img
            """, inference);

        Assert.That(lines, Is.EqualTo("1"));
        var (data, shape) = inference.Tensors.Single();
        float[] R = [114, 114, 114, 114, 114, 114, 114, 114,
                     250, 250, 250, 250, 250, 250, 250, 250,
                     40, 40, 40, 40, 40, 40, 40, 40,
                     114, 114, 114, 114, 114, 114, 114, 114];
        float[] G = [114, 114, 114, 114, 114, 114, 114, 114,
                     20, 20, 20, 20, 20, 20, 20, 20,
                     50, 50, 50, 50, 50, 50, 50, 50,
                     114, 114, 114, 114, 114, 114, 114, 114];
        float[] B = [114, 114, 114, 114, 114, 114, 114, 114,
                     30, 30, 30, 30, 30, 30, 30, 30,
                     60, 60, 60, 60, 60, 60, 60, 60,
                     114, 114, 114, 114, 114, 114, 114, 114];
        Assert.Multiple(() =>
        {
            Assert.That(shape, Is.EqualTo(new[] { 1, 3, 4, 8 }));
            Assert.That(data, Is.EqualTo(R.Concat(G).Concat(B).ToArray()));
        });
    }

    [Test]
    public void Letterbox_NarrowSource_PadsHorizontally()
    {
        // 2×4 → 8×4：scale = min(4, 1) = 1，newW = 2，padX = (8-2)/2 = 3（左右各 3 列 114 垫）。
        // 列序：[pad×3, col0, col1, pad×3]；col0 RGB(250,0,0)、col1 RGB(0,0,250)。
        using var mat = new Mat(4, 2, MatType.CV_8UC3);
        Cv2.Rectangle(mat, new Rect(0, 0, 1, 4), new Scalar(0, 0, 250), -1);  // BGR → R=250
        Cv2.Rectangle(mat, new Rect(1, 0, 1, 4), new Scalar(250, 0, 0), -1);  // BGR → B=250
        var image = Convert.ToBase64String(mat.ToBytes(".png"));

        var inference = new StubInference();
        Run($$"""
            $img = NET_IMAGE("{{image}}", 8, 4, "letter")
            PRINT $img
            """, inference);

        var (data, shape) = inference.Tensors.Single();
        var row = new float[] { 114, 114, 114, 250, 0, 114, 114, 114 };
        Assert.Multiple(() =>
        {
            Assert.That(shape, Is.EqualTo(new[] { 1, 3, 4, 8 }));
            // R 面：col0 = 250，col1 = 0
            Assert.That(data.Take(32), Is.EqualTo(Enumerable.Range(0, 4).SelectMany(_ => row).ToArray()));
            // G 面：两列全 0
            Assert.That(data.Skip(32).Take(32), Is.EqualTo(Enumerable.Range(0, 4)
                .SelectMany(_ => new float[] { 114, 114, 114, 0, 0, 114, 114, 114 }).ToArray()));
            // B 面：col0 = 0，col1 = 250
            Assert.That(data.Skip(64), Is.EqualTo(Enumerable.Range(0, 4)
                .SelectMany(_ => new float[] { 114, 114, 114, 0, 250, 114, 114, 114 }).ToArray()));
        });
    }

    // ---- std YOLO_DETECT 闭环：过滤 + 逆映射 + 句柄释放 ----

    [Test]
    public void YoloDetect_ClosedLoop_FiltersAndReverseMaps()
    {
        // ROI (10,20) 8×4 → 640×640：scale = 640/8 = 80，newW=640，newH=INT(4*80+0.5)=320，
        // padX=0，padY=(640-320)/2=160。
        // hit0 letterbox [100,200,300,400] → 屏幕 x1=(100-0)/80+10=11.25→11，
        //   y1=(200-160)/80+20=20.5→21（远离零取整），x2=13.75→14，y2=23.0→23。
        // hit1 置信度 0.1 < 0.5 被滤除；hit2 → (16,22,18,22)。
        // hit3 越界框（模型伪影）→ ROI 裁剪：x1=2.25→2→10，y1=3.25→3→20，x2=26.25→26→18，y2=35.5→36→24。
        var inference = EndToEnd(
            [100, 200, 300, 400, 0.9f, 3],
            [0, 0, 0, 0, 0.1f, 1],
            [500, 300, 600, 320, 0.7f, 0],
            [-620, -1180, 1300, 1400, 0.6f, 2]);
        var lines = Run("""
            $n = NET_LOAD("stub")
            $c = YOLO_DETECT($n, 10, 20, 8, 4, 0.5)
            PRINT $c
            $k = YOLO_COUNT()
            PRINT $k
            $r0 = INT(YOLO_SCORE(0) * 10.00 + 0.5)
            PRINT $r0
            $cls0 = YOLO_CLS(0)
            PRINT $cls0
            $x1 = YOLO_X1(0)
            PRINT $x1
            $y1 = YOLO_Y1(0)
            PRINT $y1
            $x2 = YOLO_X2(0)
            PRINT $x2
            $y2 = YOLO_Y2(0)
            PRINT $y2
            $r1 = INT(YOLO_SCORE(1) * 10.00 + 0.5)
            PRINT $r1
            $cls1 = YOLO_CLS(1)
            PRINT $cls1
            $r2 = INT(YOLO_SCORE(2) * 10.00 + 0.5)
            PRINT $r2
            $cls2 = YOLO_CLS(2)
            PRINT $cls2
            $x1b = YOLO_X1(2)
            PRINT $x1b
            $y1b = YOLO_Y1(2)
            PRINT $y1b
            $x2b = YOLO_X2(2)
            PRINT $x2b
            $y2b = YOLO_Y2(2)
            PRINT $y2b
            """, inference, capture: Convert.ToBase64String(new Mat(4, 8, MatType.CV_8UC3, new Scalar(30, 20, 250)).ToBytes(".png")));

        Assert.That(lines, Is.EqualTo("3\n3\n9\n3\n11\n21\n14\n23\n7\n0\n6\n2\n10\n20\n18\n24"));

        // letterbox 张量：[1,3,640,640]，pad 行 114、内容行 = 均匀色 RGB(250,20,30)（8×4 → 640×320 居中）。
        var (data, shape) = inference.Tensors[0];
        Assert.Multiple(() =>
        {
            Assert.That(shape, Is.EqualTo(new[] { 1, 3, 640, 640 }));
            Assert.That(data[0 * 640 * 640 + 0], Is.EqualTo(114f));       // 首行 = 上垫
            Assert.That(data[0 * 640 * 640 + 200 * 640], Is.EqualTo(250f)); // 内容带 R
            Assert.That(data[1 * 640 * 640 + 200 * 640], Is.EqualTo(20f));  // 内容带 G
            Assert.That(data[2 * 640 * 640 + 200 * 640], Is.EqualTo(30f));  // 内容带 B
            Assert.That(data[0 * 640 * 640 + 639 * 640], Is.EqualTo(114f)); // 末行 = 下垫
            // 归一化张量（NET_SCALE 1/255）
            Assert.That(inference.Tensors[1].Data[0], Is.EqualTo(114f / 255f).Within(1e-4));
        });
        Assert.That(inference.Freed, Is.EqualTo(2), "norm 与 img 张量必须释放");
    }

    // ---- 防呆与边界 ----

    [Test]
    public void YoloDetect_NonEndToEndShape_ReturnsMinus1()
    {
        // 一对多导出 [1,8400,85]：洞内形状守卫 → -1，且缓存清空（访问器中性）。
        var inference = new StubInference { Output = new float[8400 * 85], Shape = [1, 8400, 85] };
        var lines = Run("""
            $n = NET_LOAD("stub")
            $c = YOLO_DETECT($n, 0, 0, 8, 4, 0.5)
            PRINT $c
            $k = YOLO_COUNT()
            PRINT $k
            $cls = YOLO_CLS(0)
            PRINT $cls
            """, inference, capture: Convert.ToBase64String(new Mat(4, 8, MatType.CV_8UC3, new Scalar(30, 20, 250)).ToBytes(".png")));

        Assert.That(lines, Is.EqualTo("-1\n0\n-1"));
    }

    [Test]
    public void YoloDetect_EmptyOutput_ReturnsZeroHits()
    {
        // 端到端形状但 0 行（如空画面）：0 命中（区别于形状错误的 -1）。
        var inference = new StubInference { Output = [], Shape = [1, 0, 6] };
        var lines = Run("""
            $n = NET_LOAD("stub")
            $c = YOLO_DETECT($n, 0, 0, 8, 4, 0.5)
            PRINT $c
            $k = YOLO_COUNT()
            PRINT $k
            $cls = YOLO_CLS(0)
            PRINT $cls
            """, inference, capture: Convert.ToBase64String(new Mat(4, 8, MatType.CV_8UC3, new Scalar(30, 20, 250)).ToBytes(".png")));

        Assert.That(lines, Is.EqualTo("0\n0\n-1"));
    }
}
