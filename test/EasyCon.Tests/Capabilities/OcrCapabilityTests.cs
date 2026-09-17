using EasyCon.Capture;
using EasyCon.Capture.Ocr;
using EasyCon.Core.Capabilities;
using EasyCon.Core.Runner;
using EasyCon.Core.Script;
using EasyCon.Script;
using EasyCon.Tests.Support;
using EasyScript;
using System.Collections.Immutable;

namespace EasyCon.Tests.Capabilities;

/// <summary>
/// P3 OCR 能力面：
/// __OCR_INIT__/__OCR__ 洞转发 IOcrService（旧签名映射 OcrConfig）、
/// 旧 OcrDelegate 直通（单次调用、无双重采帧）、
/// Tesseract 后端与旧路径同图同区域对拍（原生库/模型缺失时 Ignore）。
/// </summary>
[TestFixture]
public class OcrCapabilityTests
{
    const string Script = """
        $t = OCR(1, 2, 30, 8, "eng")
        PRINT $t
        $c = OCR_CONF()
        PRINT $c
        """;

    static readonly string AppTessdata = AppDomain.CurrentDomain.BaseDirectory + "/Tessdata";

    sealed class FakeCaptureSource(string frame) : ICaptureSource
    {
        public List<int[]> Regions = new();

        public string? CaptureFrame(int x, int y, int width, int height)
        {
            Regions.Add([x, y, width, height]);
            return frame;
        }
    }

    sealed class FakeOcrService : IOcrService
    {
        public List<OcrConfig> Inits = new();
        public List<(string Image, int X, int Y, int W, int H, string? Lang)> Recognitions = new();
        public string Text = "HELLO";
        public int Confidence = 77;

        public string Backend => "fake";
        public int LastConfidence => Confidence;

        public bool Init(OcrConfig cfg)
        {
            Inits.Add(cfg);
            return true;
        }

        public string Recognize(ImageRef image, OcrQuery query)
        {
            Recognitions.Add((image.Base64, query.X, query.Y, query.Width, query.Height, query.Language));
            return Text;
        }

        public void Dispose() { }
    }

    /// <summary>编译脚本会话（引擎面）。</summary>
    static IScriptSession CompileSession(string script)
    {
        var engine = new EasyScriptEngine();
        return engine.FromSource(script,
            new ScriptHostOptions { Compile = new CompileOptions { ExtVars = ImmutableHashSet<string>.Empty, UseDiskCache = false } });
    }

    [Test]
    public void OcrHoles_ForwardToIOcrService()
    {
        var ocr = new FakeOcrService();
        var capture = new FakeCaptureSource("RlJBTUU=");
        var session = CompileSession(Script);

        var io = new RecordingIo();
        var caps = new CapabilitySet
        {
            Console = new ConsoleIoAdapter(io),
            Capture = capture,
            Ocr = ocr,
        };
        session.Run(new CancellationTokenSource().Token, caps);

        // __OCR_INIT__：StdLib 默认签名 → OcrConfig 映射
        Assert.That(ocr.Inits.Count, Is.EqualTo(1), "OCR_INIT 应触发一次 Init");
        var cfg = ocr.Inits[0];
        Assert.That(cfg.Language, Is.EqualTo("eng"));
        Assert.That(cfg.ModelPath, Is.EqualTo(AppTessdata));
        Assert.That(cfg.Options["tess:engineMode"], Is.EqualTo("DEFAULT"));
        Assert.That(cfg.Options["tess:psmode"], Is.EqualTo("SINGLE_LINE"));

        // __OCR__：区域采集一次，识别服务收整图（query 缺省区域）+ 语言
        Assert.That(capture.Regions, Is.EqualTo(new[] { new[] { 1, 2, 30, 8 } }));
        Assert.That(ocr.Recognitions.Count, Is.EqualTo(1));
        var (image, x, y, w, h, lang) = ocr.Recognitions[0];
        Assert.That(image, Is.EqualTo("RlJBTUU="));
        Assert.That((x, y, w, h, lang), Is.EqualTo((0, 0, 0, 0, "eng")));

        // PRINT 文本 + OCR_CONF 置信度
        Assert.That(io.Lines, Is.EqualTo(new[] { "HELLO", "77" }));
    }

    [Test]
    public void LegacyOcrDelegate_PassthroughRegion_SingleCall()
    {
        var initCalls = new List<string>();
        var recognizeCalls = new List<string>();
        var legacyInit = new OcrInitDelegate((lang, path, mode, psm) =>
        {
            initCalls.Add($"{lang}|{path}|{mode}|{psm}");
            return true;
        });
        var legacyOcr = new OcrDelegate((x, y, w, h, lang) =>
        {
            recognizeCalls.Add($"{x},{y},{w},{h},{lang}");
            return "LEG";
        });

        var session = CompileSession(Script);

        var io = new RecordingIo();
        var caps = new CapabilitySet
        {
            Console = new ConsoleIoAdapter(io),
            Ocr = new DelegateOcrService(legacyOcr, legacyInit, () => 55),
        };
        session.Run(new CancellationTokenSource().Token, caps);

        // 旧委托直通：区域原样传入且仅一次（无双重采帧）；置信度走旧 ocrConf
        Assert.That(recognizeCalls, Is.EqualTo(new[] { "1,2,30,8,eng" }));
        Assert.That(initCalls, Is.EqualTo(new[] { $"eng|{AppTessdata}|DEFAULT|SINGLE_LINE" }));
        Assert.That(io.Lines, Is.EqualTo(new[] { "LEG", "55" }));
    }

    [Test]
    public void TesseractBackend_MatchesLegacyPath_OnSameImage()
    {
        var tessdata = FindTessdata();
        if (tessdata == null)
            Assert.Ignore("未找到 tessdata（eng.traineddata），跳过 Tesseract 对拍");

        // 合成图：黑底白字（EzCv），新旧路径消费同一张 PNG
        using var mat = new OpenCvSharp.Mat(100, 340, OpenCvSharp.MatType.CV_8UC3);
        unsafe
        {
            new Span<byte>((void*)mat.Data, 100 * 340 * 3).Clear();   // 黑底（Mat 构造不清零）
        }
        OpenCvSharp.Cv2.PutText(mat, "HELLO 123", new OpenCvSharp.Point(15, 70),
            OpenCvSharp.HersheyFonts.HersheySimplex, 1.8, OpenCvSharp.Scalar.White, 2);
        var png = mat.ToBytes(".png");
        var base64 = Convert.ToBase64String(png);

        // 旧路径：OcrEngineCache + TesseractEngineFactory 识别器直接吃 PNG
        var legacyCache = new OcrEngineCache { DefaultDataPath = tessdata };
        IOcrRecognizer legacyRecognizer;
        try
        {
            legacyRecognizer = new TesseractEngineFactory().CreateRecognizer("eng", tessdata, "DEFAULT", "SINGLE_LINE");
            legacyRecognizer.Recognize(png);
        }
        catch (Exception ex)
        {
            Assert.Ignore($"Tesseract 原生库不可用，跳过对拍：{ex.Message}");
            return;
        }

        string legacyFull = legacyRecognizer.Recognize(png).Text;
        using (var roi = new OpenCvSharp.Mat(mat, new OpenCvSharp.Rect(10, 0, 150, 100)))
        {
            var legacyRoi = legacyRecognizer.Recognize(roi.ToBytes(".png")).Text;
            legacyRecognizer.Dispose();

            // 新路径：TesseractOcrService（独立缓存实例）
            using var service = new TesseractOcrService(new OcrEngineCache { DefaultDataPath = tessdata });
            Assert.That(service.Init(new OcrConfig
            {
                Language = "eng",
                ModelPath = tessdata,
                Options = new Dictionary<string, string>
                {
                    ["tess:engineMode"] = "DEFAULT",
                    ["tess:psmode"] = "SINGLE_LINE",
                },
            }), Is.True, "TesseractOcrService.Init 失败");

            var full = service.Recognize(ImageRef.FromBase64(base64), new OcrQuery { Language = "eng" });
            var roiHit = service.Recognize(ImageRef.FromBase64(base64),
                new OcrQuery { X = 10, Y = 0, Width = 150, Height = 100, Language = "eng" });

            TestContext.Out.WriteLine($"legacy-full='{legacyFull}' service-full='{full}'");
            TestContext.Out.WriteLine($"legacy-roi='{legacyRoi}' service-roi='{roiHit}'");

            // 同图同区域：新旧路径结果一致
            Assert.That(full, Is.EqualTo(legacyFull));
            Assert.That(roiHit, Is.EqualTo(legacyRoi));
        }
    }

    /// <summary>向上探测仓库内 tessdata 目录（traineddata 不入库，按机器现场探测）。</summary>
    static string? FindTessdata()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && dir != null; i++, dir = Path.GetDirectoryName(dir))
        {
            foreach (var candidate in new[]
                     {
                         Path.Combine(dir, "src", "EasyCon.Capture", "bin", "Debug", "net8.0", "Tessdata"),
                         Path.Combine(dir, "Depend", "opencvsharp", "test", "OpenCvSharp.Tests", "_data", "tessdata"),
                     })
            {
                if (File.Exists(Path.Combine(candidate, "eng.traineddata")))
                    return candidate;
            }
        }
        return null;
    }
}