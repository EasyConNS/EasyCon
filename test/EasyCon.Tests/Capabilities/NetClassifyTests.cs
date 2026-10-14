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
/// V1 分类链路（ResNet 类模型）：NET_ARGMAX/NET_ROWS 末维=类别维修正（[1,N] 向量输出
/// 可解码）、NET_NORM 逐通道归一化、NET_LABELS/NET_LABEL 标签表、NET_IMAGE_FILE +
/// "rgbc" cover 裁剪，以及方案 V1 全景走查闭环。
/// stub 推理喂预设输出；图片/标签走临时文件。
/// </summary>
[TestFixture]
public class NetClassifyTests
{
    /// <summary>stub：记录张量（真实 Normalize），RunHeld 返回预设 logits。</summary>
    sealed class ClassifyInference : IInference
    {
        public float[] Logits = [];
        public int[] Shape = [1, 1];
        public List<(float[] Data, int[] Shape)> Tensors { get; } = [];

        public int Load(string modelPath) => 5;
        public int[]? LastOutputShape => Shape;
        public int HoldTensor(float[] data, int[] shape)
        {
            Tensors.Add(((float[])data.Clone(), (int[])shape.Clone()));
            return Tensors.Count;
        }
        public void FreeTensor(int handle) { }
        public float[]? RunHeld(int session, int tensorHandle) => Logits;

        public int TransformTensor(int handle, float scale, float offset)
        {
            var (data, shape) = Tensors[handle - 1];
            return HoldTensor(data.Select(v => v * scale + offset).ToArray(), shape);
        }

        public int Normalize(int handle, float[] mean, float[] std)
        {
            var (data, shape) = Tensors[handle - 1];
            int channels = shape.Length >= 4 ? shape[1] : shape.Length == 3 ? shape[0] : 1;
            if (data.Length % channels != 0
                || (mean.Length != 1 && mean.Length != channels)
                || (std.Length != 1 && std.Length != channels))
                return -1;
            int plane = data.Length / channels;
            return HoldTensor(data.Select((v, i) =>
            {
                int c = i / plane % channels;
                float m = mean.Length == 1 ? mean[0] : mean[c];
                float s = std.Length == 1 ? std[0] : std[c];
                return (v - m) / s;
            }).ToArray(), shape);
        }

        public void Unload(int session) { }
        public void Dispose() { }
    }

    static string Run(string source, ClassifyInference inference, out RecordingIo io, string? capture = null)
    {
        var engine = new EasyScriptEngine();
        var session = engine.FromSource(source,
            new ScriptHostOptions
            {
                Compile = new CompileOptions { ExtVars = ImmutableHashSet<string>.Empty, UseDiskCache = false },
            });
        Assert.That(session.Info.Diagnostics.Where(d => d.IsError), Is.Empty, "脚本必须编译通过");
        io = new RecordingIo();
        var caps = new CapabilitySet { Console = new ConsoleIoAdapter(io), Inference = inference };
        if (capture != null)
            caps.Capture = new DelegateCaptureSource((_, _, _, _) => capture);
        session.Run(new CancellationTokenSource().Token, caps);
        return string.Join("\n", io.Lines);
    }

    // ---- 缺口 3：末维 = 类别维 ----

    [Test]
    public void ArgMax_VectorOutput_DecodesSingleRow()
    {
        // [1,1000]：旧实现 shape.Length<3 恒返 -1；修正后 1 行 1000 列，NET_ROWS = 1
        var logits = new float[1000];
        logits[742] = 3.5f;
        var inference = new ClassifyInference { Logits = logits, Shape = [1, 1000] };
        var lines = Run("""
            $n = NET_LOAD("stub")
            $r = NET_RUNH($n, 9)
            $rows = NET_ROWS()
            PRINT $rows
            $a = NET_ARGMAX(0)
            PRINT $a
            """, inference, out _);

        Assert.That(lines, Is.EqualTo("1\n742"));
    }

    [Test]
    public void ArgMax_OneDimOutput_DecodesSingleRow()
    {
        var logits = new float[5];
        logits[3] = 1f;
        var inference = new ClassifyInference { Logits = logits, Shape = [5] };
        var lines = Run("""
            $r = NET_RUNH(5, 9)
            $rows = NET_ROWS()
            PRINT $rows
            $a = NET_ARGMAX(0)
            PRINT $a
            """, inference, out _);

        Assert.That(lines, Is.EqualTo("1\n3"));
    }

    [Test]
    public void ArgMax_Rank3_Unchanged()
    {
        // [1,T,C]：修正前后同语义（T 行 C 列，逐行 argmax）
        float[] logits = [-1, 5, -1, -1, -1, -1, -1, -1, 4, -1, -1, -1, -1, -1, -1];
        var inference = new ClassifyInference { Logits = logits, Shape = [1, 3, 5] };
        var lines = Run("""
            $r = NET_RUNH(5, 9)
            $rows = NET_ROWS()
            PRINT $rows
            $a0 = NET_ARGMAX(0)
            PRINT $a0
            $a1 = NET_ARGMAX(1)
            PRINT $a1
            """, inference, out _);

        Assert.That(lines, Is.EqualTo("3\n1\n3"));
    }

    // ---- 缺口 1：NET_NORM 逐通道 ----

    [Test]
    public void NetNorm_PerChannel_NormalizesByChannel()
    {
        // [1,3,1,2] 张量，通道均值/方差各自作用：plane = 2，c = (i/2)%3
        var inference = new ClassifyInference();
        inference.HoldTensor([1f, 2f, 10f, 20f, 100f, 200f], [1, 3, 1, 2]);   // 句柄 1
        Run($$"""
            $h = NET_NORM(1, [{{0.2}}, {{0.4}}, {{0.6}}], [{{0.1}}, {{0.2}}, {{0.5}}])
            PRINT $h
            """, inference, out _);

        Assert.That(inference.Tensors, Has.Count.EqualTo(2));
        var (data, shape) = inference.Tensors[1];
        float[] input = [1f, 2f, 10f, 20f, 100f, 200f];
        Assert.Multiple(() =>
        {
            Assert.That(shape, Is.EqualTo(new[] { 1, 3, 1, 2 }));
            Assert.That(data, Is.EqualTo(new[]
            {
                (input[0] - 0.2f) / 0.1f, (input[1] - 0.2f) / 0.1f,
                (input[2] - 0.4f) / 0.2f, (input[3] - 0.4f) / 0.2f,
                (input[4] - 0.6f) / 0.5f, (input[5] - 0.6f) / 0.5f,
            }));
        });
    }

    [Test]
    public void NetNorm_LengthMismatch_ReturnsMinus1()
    {
        // mean 长度 2 ≠ 通道数 3 且 ≠ 1 → 优雅失败
        var inference = new ClassifyInference();
        var lines = Run("""
            $h = NET_NORM(1, [0.10, 0.20], [0.10, 0.20, 0.30])
            PRINT $h
            """, inference, out _);

        Assert.That(lines, Is.EqualTo("-1"));
    }

    // ---- 缺口 2：标签表 ----

    [Test]
    public void Labels_Roundtrip_AndReload()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"easycon-labels-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        // ECS 字符串会解析反斜杠转义；插入脚本的文件路径使用正斜杠。
        var labels1 = Path.Combine(dir, "a.txt").Replace('\\', '/');
        var labels2 = Path.Combine(dir, "b.txt").Replace('\\', '/');
        File.WriteAllLines(labels1, ["tench", "goldfish", "", "shark"]);   // 空行占位 → 4 条
        File.WriteAllLines(labels2, ["cat", "dog"]);

        try
        {
            var inference = new ClassifyInference();
            var lines = Run($$"""
                $c1 = NET_LABELS("{{labels1}}")
                PRINT $c1
                $l0 = NET_LABEL(0)
                PRINT $l0
                $l2 = NET_LABEL(2)
                PRINT $l2
                $l3 = NET_LABEL(3)
                PRINT $l3
                $c2 = NET_LABELS("{{labels2}}")
                PRINT $c2
                $l1b = NET_LABEL(1)
                PRINT $l1b
                $lo = NET_LABEL(5)
                PRINT $lo
                """, inference, out _);

            // 行号即索引（空行占位，保字典行号语义）；重复加载整体替换，越界 → ""
            Assert.That(lines, Is.EqualTo("4\ntench\n\nshark\n2\ndog\n"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public void Labels_MissingFile_ReturnsMinus1()
    {
        var inference = new ClassifyInference();
        var lines = Run("""
            $c = NET_LABELS("no/such/file.txt")
            PRINT $c
            $l = NET_LABEL(0)
            PRINT $l
            """, inference, out _);

        Assert.That(lines, Is.EqualTo("-1\n"));
    }

    // ---- 缺口 A/C：NET_IMAGE_FILE + crop ----

    [Test]
    public void NetImageFile_Rgbc_CoversAndCenterCrops()
    {
        // 4 宽 × 8 高（左半 2 列红、右半 2 列蓝）→ 目标 4×4：scale = max(4/4, 4/8) = 1
        // → 4×8，居中裁 y∈[2,6)：像素零缩放（免插值混色），R 面 = [250,250,0,0]×4 行，
        // B 面 = [0,0,250,250]×4 行——不变形（列序保持）、不垫边（无 114）。
        var dir = Path.Combine(Path.GetTempPath(), $"easycon-img-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var imagePath = Path.Combine(dir, "t.png").Replace('\\', '/');
        using (var mat = new Mat(8, 4, MatType.CV_8UC3))
        {
            Cv2.Rectangle(mat, new Rect(0, 0, 2, 8), new Scalar(0, 0, 250), -1);
            Cv2.Rectangle(mat, new Rect(2, 0, 2, 8), new Scalar(250, 0, 0), -1);
            File.WriteAllBytes(imagePath, mat.ToBytes(".png"));
        }

        try
        {
            var inference = new ClassifyInference();
            Run($$"""
                $h = NET_IMAGE_FILE("{{imagePath}}", 4, 4, "rgbc")
                PRINT $h
                """, inference, out _);

            var (data, shape) = inference.Tensors.Single();
            float[] R = [250, 250, 0, 0];
            Assert.Multiple(() =>
            {
                Assert.That(shape, Is.EqualTo(new[] { 1, 3, 4, 4 }));
                Assert.That(data.Take(16).ToArray(), Is.EqualTo(Enumerable.Range(0, 4).SelectMany(_ => R).ToArray()));
                Assert.That(data.Skip(16).Take(16).ToArray(), Is.EqualTo(new float[16]), "G 全 0");
                Assert.That(data.Skip(32).ToArray(), Is.EqualTo(Enumerable.Range(0, 4)
                    .SelectMany(_ => new float[] { 0, 0, 250, 250 }).ToArray()));
                Assert.That(data, Has.None.EqualTo(114f), "crop 不应有垫边");
            });
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public void NetImageFile_LetterC_NotSupported_ReturnsZero()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"easycon-img-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var imagePath = Path.Combine(dir, "t.png").Replace('\\', '/');
        using (var mat = new Mat(4, 4, MatType.CV_8UC3, new Scalar(30, 20, 250)))
            File.WriteAllBytes(imagePath, mat.ToBytes(".png"));
        try
        {
            var inference = new ClassifyInference();
            var lines = Run($$"""
                $h = NET_IMAGE_FILE("{{imagePath}}", 4, 4, "letterc")
                PRINT $h
                """, inference, out _);

            Assert.That(lines, Is.EqualTo("0"));
            Assert.That(inference.Tensors, Is.Empty);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---- std 配方：PP_OCR_TEXT（整字典标签表折叠）----

    [Test]
    public void PpocrText_FoldsFullDictionary_WithDedupAndBlank()
    {
        // 字典（PP-OCR 约定：blank=0 不占行，class c → NET_LABEL(c-1) = 行 c-1）：
        // 行 0:"L" 1:"v" 2:"A" 3:"B" 4:"C" → class 1..5
        // 逐帧 argmax 序列 [1,2,3,3,0,4,5,5] → "Lv" + 连续 A 去重 + blank 重置 + "BC"，
        // 整字典无窗口——数字配方（PP_OCR_DIGITS）过滤掉的字母全部保留。
        var dir = Path.Combine(Path.GetTempPath(), $"easycon-ptext-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var dictPath = Path.Combine(dir, "dict.txt").Replace('\\', '/');
        File.WriteAllLines(dictPath, ["L", "v", "A", "B", "C"]);

        // [1,T,C] one-hot：帧 i 在 classes[i] 处置 1
        int[] classes = [1, 2, 3, 3, 0, 4, 5, 5];
        const int cols = 8;
        var logits = new float[classes.Length * cols];
        for (int i = 0; i < classes.Length; i++)
        {
            logits[i * cols + classes[i]] = 1f;
            for (int c = 0; c < cols; c++)
                if (c != classes[i])
                    logits[i * cols + c] = -1f;
        }
        var inference = new ClassifyInference { Logits = logits, Shape = [1, classes.Length, cols] };

        try
        {
            var lines = Run($$"""
                $n = NET_LOAD("stub")
                $s = PP_OCR_TEXT($n, 0, 0, 64, 32, "{{dictPath}}")
                PRINT $s
                $l1 = NET_LABEL(1)
                PRINT $l1
                """, inference, out _,
                capture: Convert.ToBase64String(new Mat(64, 32, MatType.CV_8UC3, new Scalar(30, 20, 250)).ToBytes(".png")));

            // 配方折叠 "LvABC"；NET_LABELS 全局表被配方整体替换（NET_LABEL(1)="v" 可查）
            Assert.That(lines, Is.EqualTo("LvABC\nv"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---- V1 全景走查（方案 §四 的 C# 参考一比一翻译）----

    [Test]
    public void Walkthrough_Classify_EndToEnd()
    {
        // NET_LOAD → NET_IMAGE_FILE(224,224,"rgbc") → NET_SCALE(1/255,0) → NET_NORM
        // → NET_RUNH → NET_ARGMAX(0)（已修二维）→ NET_LABELS/NET_LABEL → 类名
        var dir = Path.Combine(Path.GetTempPath(), $"easycon-walk-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var imagePath = Path.Combine(dir, "img.png").Replace('\\', '/');
        var labelsPath = Path.Combine(dir, "classes.txt").Replace('\\', '/');
        using (var mat = new Mat(200, 300, MatType.CV_8UC3, new Scalar(40, 40, 40)))
            File.WriteAllBytes(imagePath, mat.ToBytes(".png"));
        File.WriteAllLines(labelsPath, ["tench", "goldfish", "shark"]);

        try
        {
            // [1,1000] 输出，第 1 类（goldfish）最大
            var logits = new float[1000];
            logits[1] = 4f;
            logits[0] = 1f;
            var inference = new ClassifyInference { Logits = logits, Shape = [1, 1000] };

            var lines = Run($$"""
                $net = NET_LOAD("stub")
                $t = NET_SCALE(NET_IMAGE_FILE("{{imagePath}}", 224, 224, "rgbc"), 1.00 / 255, 0)
                $t = NET_NORM($t, [{{0.485}}, {{0.456}}, {{0.406}}], [{{0.229}}, {{0.224}}, {{0.225}}])
                $n = NET_RUNH($net, $t)
                $c = NET_LABELS("{{labelsPath}}")
                $cls = NET_ARGMAX(0)
                $name = NET_LABEL($cls)
                PRINT $cls
                PRINT $name
                """, inference, out _);

            Assert.That(lines, Is.EqualTo("1\ngoldfish"));

            // 链路产物检查：rgbc 图像 → NET_SCALE(1/255) → NET_NORM，三个张量同形状
            Assert.That(inference.Tensors, Has.Count.EqualTo(3));
            Assert.That(inference.Tensors[0].Shape, Is.EqualTo(new[] { 1, 3, 224, 224 }));
            Assert.That(inference.Tensors[1].Shape, Is.EqualTo(new[] { 1, 3, 224, 224 }));
            Assert.That(inference.Tensors[2].Shape, Is.EqualTo(new[] { 1, 3, 224, 224 }));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}