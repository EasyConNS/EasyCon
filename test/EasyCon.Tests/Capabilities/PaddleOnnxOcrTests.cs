using EasyCon.Capture;
using EasyCon.Capture.Ocr;
using EasyCon.Core.Capabilities;
using OpenCvSharp;

namespace EasyCon.Tests.Capabilities;

/// <summary>
/// P4 PaddleOCR-ONNX 后端：
/// fake 工厂锁定 Init/Recognize/LastConfidence 管线（无外部依赖）；
/// PP-OCRv4/v5 模型在场时与 Tesseract 后端同图对比（精度/耗时报告输出到 stdout，模型缺失时 Ignore）。
/// </summary>
[TestFixture]
public class PaddleOnnxOcrTests
{
    /// <summary>记录调用的假工厂/识别器（模型无关，验证服务管线）。</summary>
    sealed class StubRecognizer(string text, float confidence) : IOcrRecognizer
    {
        public int Calls;
        public string? LastLang;

        public OcrRecognizeResult Recognize(byte[] image)
        {
            Calls++;
            return new OcrRecognizeResult(text, confidence);
        }

        public void Dispose() { }
    }

    sealed class StubFactory(StubRecognizer recognizer) : IOcrEngineFactory
    {
        public List<string> RequestedLangs = new();

        public IOcrRecognizer CreateRecognizer(string lang, string dataPath, string engineMode, string psmode)
        {
            RequestedLangs.Add(lang);
            return recognizer;
        }

        public IOcrEngine? CreateEngine(string lang, string dataPath, string engineMode, string psmode) => null;
    }

    static byte[] TinyPng()
    {
        using var mat = new Mat(4, 4, MatType.CV_8UC3);
        return mat.ToBytes(".png");
    }

    [Test]
    public void PaddleOnnxOcr_Plumbing_WithStubFactory()
    {
        var recognizer = new StubRecognizer("PADDLE", 0.92f);
        var factory = new StubFactory(recognizer);
        using var service = new PaddleOnnxOcr("/models", cache: new OcrEngineCache(factory));

        Assert.That(service.Backend, Is.EqualTo("paddle-onnx"));

        // Init 映射：Language → CreateRecognizer(lang)，ModelPath → dataPath；同参数幂等跳过
        Assert.That(service.Init(new OcrConfig { Language = "eng", ModelPath = "/models" }), Is.True);
        Assert.That(service.Init(new OcrConfig { Language = "eng", ModelPath = "/models" }), Is.True);
        Assert.That(factory.RequestedLangs, Is.EqualTo(new[] { "eng" }));

        var text = service.Recognize(ImageRef.FromBase64(Convert.ToBase64String(TinyPng())),
            new OcrQuery { Language = "eng" });
        Assert.That(text, Is.EqualTo("PADDLE"));
        Assert.That(recognizer.Calls, Is.EqualTo(1));
        Assert.That(service.LastConfidence, Is.EqualTo(92));
    }

    [Test]
    public void PaddleOnnxOcr_VsTesseract_OnSameImage()
    {
        var modelDir = FindPaddleModelDir();
        if (modelDir == null)
            Assert.Ignore("PaddleOCR ONNX 模型缺失（PP-OCRv5_mobile_det/rec.onnx），跳过对拍");

        // 合成图：黑底白字（与 P3 对拍同一构造）
        using var mat = new Mat(100, 340, MatType.CV_8UC3);
        unsafe
        {
            new Span<byte>((void*)mat.Data, 100 * 340 * 3).Clear();
        }
        Cv2.PutText(mat, "HELLO 123", new Point(15, 70),
            HersheyFonts.HersheySimplex, 1.8, Scalar.White, 2);
        var base64 = Convert.ToBase64String(mat.ToBytes(".png"));

        using var paddle = new PaddleOnnxOcr(modelDir);
        Assert.That(paddle.Init(new OcrConfig { Language = "eng" }), Is.True, "PaddleOnnxOcr.Init 失败");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var paddleText = paddle.Recognize(ImageRef.FromBase64(base64), new OcrQuery { Language = "eng" });
        long paddleMs = sw.ElapsedMilliseconds;
        int paddleConf = paddle.LastConfidence;

        TestContext.Out.WriteLine($"paddle-onnx : text='{paddleText}' conf={paddleConf} first-call={paddleMs}ms");

        // Tesseract 腿：环境不可用只报告，不失败（P3 已有独立对拍）
        var tessdata = FindDirWith("eng.traineddata");
        if (tessdata == null)
        {
            TestContext.Out.WriteLine("tesseract   : tessdata 缺失，未对比");
            return;
        }

        try
        {
            using var tess = new TesseractOcrService(new OcrEngineCache { DefaultDataPath = tessdata });
            if (!tess.Init(new OcrConfig { Language = "eng", ModelPath = tessdata }))
            {
                TestContext.Out.WriteLine("tesseract   : 原生库不可用，未对比");
                return;
            }
            sw.Restart();
            var tessText = tess.Recognize(ImageRef.FromBase64(base64), new OcrQuery { Language = "eng" });
            long tessMs = sw.ElapsedMilliseconds;
            TestContext.Out.WriteLine($"tesseract   : text='{tessText}' conf={tess.LastConfidence} first-call={tessMs}ms");
        }
        catch (Exception ex)
        {
            TestContext.Out.WriteLine($"tesseract   : 加载失败（{ex.Message}），未对比");
        }

        // 合成图清晰：Paddle 结果应非空（精度数值仅供 stdout 报告）
        Assert.That(paddleText, Is.Not.Empty, "PaddleOCR 对清晰合成图识别为空");
    }

    /// <summary>向上探测模型目录：须同时含 det 与 rec ONNX 模型（外部资产，不入库）。</summary>
    static string? FindPaddleModelDir()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && dir != null; i++, dir = Path.GetDirectoryName(dir))
        {
            foreach (var candidate in new[]
                     {
                         Path.Combine(dir, "models", "paddle"),
                         Path.Combine(dir, "models"),
                         Path.Combine(dir, "PaddleOCR"),
                         Path.Combine(dir, "assets", "PaddleOCR"),
                     })
            {
                if (File.Exists(Path.Combine(candidate, "PP-OCRv5_mobile_det.onnx")))
                    return candidate;
            }
        }
        return null;
    }

    static string? FindDirWith(string fileName)
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && dir != null; i++, dir = Path.GetDirectoryName(dir))
        {
            var candidate = Path.Combine(dir, "src", "EasyCon.Capture", "bin", "Debug", "net8.0", "Tessdata");
            if (File.Exists(Path.Combine(candidate, fileName)))
                return candidate;
            candidate = Path.Combine(dir, "Depend", "opencvsharp", "test", "OpenCvSharp.Tests", "_data", "tessdata");
            if (File.Exists(Path.Combine(candidate, fileName)))
                return candidate;
        }
        return null;
    }
}