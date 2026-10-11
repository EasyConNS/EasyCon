using EasyCon.Core;
using EasyCon.Core.Capabilities;
using EasyCon.Core.Runner;
using EasyCon.Core.Script;
using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Tests.Support;
using System.Collections.Immutable;

namespace EasyCon.Tests.Capabilities;

/// <summary>
/// __CTC_FOLD__ 打洞语义（std CTC_FOLD 配方的宿主实现）：连续同类别去重、blank 帧重置、
/// charset 窗口外类别跳过（仍参与去重序列）、窗口内类别 c → charset[c-base]。
/// stub 推理直接喂 argmax 序列对应的 logits，无需真模型/截图。
/// </summary>
[TestFixture]
public class CtcFoldTests
{
    /// <summary>stub：RunHeld 返回预设 logits，LastOutputShape = [1,T,C]。</summary>
    sealed class SeqInference : IInference
    {
        public float[] Logits = [];
        public int[] Shape = [1, 1, 1];

        public int Load(string modelPath) => 5;
        public int[]? LastOutputShape => Shape;
        public int HoldTensor(float[] data, int[] shape) => 9;
        public void FreeTensor(int handle) { }
        public float[]? RunHeld(int session, int tensorHandle) => Logits;
        public int TransformTensor(int handle, float scale, float offset) => 10;
        public void Unload(int session) { }
        public void Dispose() { }
    }

    /// <summary>按逐帧 argmax 序列构造 one-hot logits：第 i 帧在 classes[i] 处置 1，其余 -1。</summary>
    static SeqInference Seq(params int[] classes)
    {
        const int cols = 24;
        var logits = new float[classes.Length * cols];
        for (int i = 0; i < classes.Length; i++)
        {
            logits[i * cols + classes[i]] = 1f;
            for (int c = 0; c < cols; c++)
                if (c != classes[i])
                    logits[i * cols + c] = -1f;
        }
        return new SeqInference { Logits = logits, Shape = [1, classes.Length, cols] };
    }

    static string FoldWith(SeqInference inference, string charset, int blank, int baseId)
    {
        const string source = """
            $n = NET_LOAD("stub")
            $r = NET_RUNH($n, 9)
            $s = CTC_FOLD(INT(ARG(0)), ARG(1), INT(ARG(2)))
            PRINT $s
            """;
        var engine = new EasyScriptEngine();
        var session = engine.FromSource(source,
            new ScriptHostOptions
            {
                Args = [blank.ToString(), charset, baseId.ToString()],
                Compile = new CompileOptions
                {
                    ExtVars = ImmutableHashSet<string>.Empty,
                    UseDiskCache = false,
                },
            });
        var io = new RecordingIo();
        session.Run(new CancellationTokenSource().Token,
            new CapabilitySet { Console = new ConsoleIoAdapter(io), Inference = inference });
        return io.Lines[^1];
    }

    static string Fold(params int[] classes) => FoldWith(Seq(classes), "0123456789", 0, 1);

    [Test]
    public void ConsecutiveDuplicates_Collapse()
    {
        // 2,3,3,4 → "123"（base=1：类别 c → 数字 c-1，连续重复折叠）
        Assert.That(Fold(2, 3, 3, 4), Is.EqualTo("123"));
    }

    [Test]
    public void Blank_ResetsCollapse()
    {
        // 2,blank,2 → "11"：blank 分隔的重复保留（CTC 语义）
        Assert.That(Fold(2, 0, 2), Is.EqualTo("11"));
        // 2,2 → "1"：连续重复折叠
        Assert.That(Fold(2, 2), Is.EqualTo("1"));
    }

    [Test]
    public void OutOfWindow_Skipped_ButParticipatesInDedup()
    {
        // 12,2,2,13,3 → "12"：窗口外类别（>base+len）不输出，但参与去重序列
        Assert.That(Fold(12, 2, 2, 13, 3), Is.EqualTo("12"));
        // 窗口外类别不重置折叠：11,2,2 → "1"（与 blank 语义相反）
        Assert.That(Fold(11, 2, 2), Is.EqualTo("1"));
    }

    [Test]
    public void CharsetWindow_Maps_ClassToChar()
    {
        // base=10、charset="AB"：类别 10→'A'、11→'B'
        Assert.That(FoldWith(Seq(10, 11, 10, 10), "AB", 0, 10), Is.EqualTo("ABA"));
    }

    [Test]
    public void EmptyOutput_ReturnsEmptyString()
    {
        var inference = new SeqInference { Logits = [], Shape = [1, 0, 24] };
        Assert.That(FoldWith(inference, "0123456789", 0, 1), Is.EqualTo(""));
    }

    [Test]
    public void PpocrV5_RealDictionaryIndices_FoldToValue()
    {
        // PP-OCRv5 字典下标配方（std PP_OCR_DIGITS 同参）：'0'-'9' = 16163-16172，blank=0。
        // 序列取自 ppocr2.png EXP 行真实 dump（1,blank,9,5,5,3,3,1）——
        // 20a089c VM 槽位污染回归的原始形状，脚本折叠应得 19531。
        const int d1 = 16164, d9 = 16172, d5 = 16168, d3 = 16166;
        const int cols = 16180;   // 容下 16k 字典类别
        var rows = new[] { 0, d1, 0, d9, d5, d5, d3, d3, d1, 0 };
        var arr = new float[rows.Length * cols];
        for (int i = 0; i < rows.Length; i++)
        {
            arr[i * cols + rows[i]] = 1f;
            for (int c = 0; c < cols; c++)
                if (c != rows[i])
                    arr[i * cols + c] = -1f;
        }
        var inference = new SeqInference { Logits = arr, Shape = [1, rows.Length, cols] };
        Assert.That(FoldWith(inference, "0123456789", 0, 16163), Is.EqualTo("19531"));
    }
}