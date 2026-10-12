using EasyCon.Core;
using EasyCon.Core.Capabilities;
using EasyCon.Core.Script;
using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Tests.Support;
using OpenCvSharp;
using System.Collections.Immutable;

namespace EasyCon.Tests.Capabilities;

/// <summary>
/// __DET_BOXES__ 洞 + DET_* 访问器（std PP_DET_FIND 配方的宿主实现）：
/// 阈值二值化、连通域、det 空间滤噪（&lt;3px）、逆映射 screen = det/scale + roi、
/// ±2px unclip、阅读序（y 优先 x）。stub 推理喂预设概率图 [1,1,H,W]，无需真模型。
/// </summary>
[TestFixture]
public class DetBoxesTests
{
    /// <summary>stub：RunHeld 返回预设概率图与形状，无视输入张量。</summary>
    sealed class DetInference : IInference
    {
        public float[] ProbMap = [];
        public int[] Shape = [1, 1, 1, 1];

        public int Load(string modelPath) => 5;
        public int[]? LastOutputShape => Shape;
        public int HoldTensor(float[] data, int[] shape) => 9;
        public void FreeTensor(int handle) { }
        public float[]? RunHeld(int session, int tensorHandle) => ProbMap;
        public int TransformTensor(int handle, float scale, float offset) => 10;
        public int Normalize(int handle, float[] mean, float[] std) => 11;
        public void Unload(int session) { }
        public void Dispose() { }
    }

    /// <summary>构造 [1,1,h,w] 概率图：矩形块区域填 value，其余 0。</summary>
    static float[] ProbMap(int h, int w, params (int X, int Y, int W, int Ht, float Value)[] blocks)
    {
        var map = new float[h * w];
        foreach (var (x, y, bw, bh, v) in blocks)
            for (int yy = y; yy < y + bh; yy++)
                for (int xx = x; xx < x + bw; xx++)
                    map[yy * w + xx] = v;
        return map;
    }

    static string Run(string source, DetInference inference, string frame)
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
            Capture = new DelegateCaptureSource((_, _, _, _) => frame),
        };
        session.Run(new CancellationTokenSource().Token, caps);
        return string.Join("\n", io.Lines);
    }

    static string MakeFrame(int w, int h)
    {
        using var mat = new Mat(h, w, MatType.CV_8UC3, new Scalar(40, 40, 40));
        return Convert.ToBase64String(mat.ToBytes(".png"));
    }

    [Test]
    public void DetFind_TwoBlobs_ReadingOrder_Unclip_AndNoiseFiltered()
    {
        // 区域 (0,0) 320×160 → scale 钳 1，det 输入 320×160。概率图（det 空间）：
        //   blob 上: x[40,90)  y[20,50)  值 0.8 → dilate(1×15) 后 rect(33,20,64,30)
        //   blob 下: x[200,280) y[100,140) 值 0.9 → dilate 后 rect(193,100,94,40)
        //   噪声:   x[150,170) y[60,70)  值 0.2 < 阈值 0.5 → 二值化即消
        // unclip（PaddleOCR 同式，v1 ratio=1.0）offset = area/perimeter，dx = round(offset/scale)：
        //   上块 offset=64×30/(2×94)=10.2→dx=10 → screen (23,10,84,50)
        //   下块 offset=94×40/(2×134)=14.0→dx=14 → screen (179,86,122,68)
        // 阅读序（y 优先）先上块。框允许超出画布（unclip 不裁剪——屏幕坐标由调用方 ROI 收敛）。
        var map = ProbMap(160, 320,
            (40, 20, 50, 30, 0.8f),
            (200, 100, 80, 40, 0.9f),
            (150, 60, 20, 10, 0.2f));
        var inference = new DetInference { ProbMap = map, Shape = [1, 1, 160, 320] };

        var lines = Run("""
            $det = NET_LOAD("stub")
            $frame = FRAME()
            $c = PP_DET_FIND($det, $frame, 0, 0, 320, 160)
            PRINT $c
            $x0 = DET_X(0)
            PRINT $x0
            $y0 = DET_Y(0)
            PRINT $y0
            $w0 = DET_W(0)
            PRINT $w0
            $h0 = DET_H(0)
            PRINT $h0
            $x1 = DET_X(1)
            PRINT $x1
            $y1 = DET_Y(1)
            PRINT $y1
            $x9 = DET_X(5)
            PRINT $x9
            """, inference, MakeFrame(320, 160));

        Assert.That(lines, Is.EqualTo("2\n23\n10\n84\n50\n179\n86\n-1"));
    }

    [Test]
    public void DetFind_NonProbMapShape_ReturnsMinus1()
    {
        // [1,300,6]（检测框布局冒充概率图）：rank-3 且通道维缺失 → 形状守卫 -1
        var inference = new DetInference { ProbMap = new float[300 * 6], Shape = [1, 300, 6] };
        var lines = Run("""
            $det = NET_LOAD("stub")
            $frame = FRAME()
            $c = PP_DET_FIND($det, $frame, 0, 0, 48, 32)
            PRINT $c
            $k = DET_COUNT()
            PRINT $k
            """, inference, MakeFrame(64, 64));

        Assert.That(lines, Is.EqualTo("-1\n0"));
    }

    [Test]
    public void DetFunctions_WithoutInference_Degrade()
    {
        // S-21 桌面降级（推理能力未装配）：NET_LOAD -1 / RUNH 0 / DET 洞与 COUNT → 0 / 访问器 → -1
        var engine = new EasyScriptEngine();
        var session = engine.FromSource("""
            $det = NET_LOAD("stub")
            PRINT $det
            $frame = FRAME()
            $c = PP_DET_FIND($det, $frame, 0, 0, 48, 32)
            PRINT $c
            $k = DET_COUNT()
            PRINT $k
            $x = DET_X(0)
            PRINT $x
            """,
            new ScriptHostOptions
            {
                Compile = new CompileOptions { ExtVars = ImmutableHashSet<string>.Empty, UseDiskCache = false },
            });
        Assert.That(session.Info.Diagnostics.Where(d => d.IsError), Is.Empty, "脚本必须编译通过");
        var io = new RecordingIo();
        var caps = new CapabilitySet
        {
            Console = new ConsoleIoAdapter(io),
            Capture = new DelegateCaptureSource((_, _, _, _) => MakeFrame(64, 64)),
        };
        session.Run(new CancellationTokenSource().Token, caps);

        // 洞 impl 在场但推理未装配 → -1（输出缺失档，Functions.md 语义）；S-21 表的 0
        // 只在原生整体缺失时生效（矩阵测试管）。DET_COUNT 读空缓存 → 0，访问器 → -1。
        Assert.That(string.Join("\n", io.Lines), Is.EqualTo("-1\n-1\n0\n-1"));
    }
}
