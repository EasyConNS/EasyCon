using EasyCon.Core.Capabilities;
using EasyCon.Core.Hosting;
using EasyCon.Core.Maa;
using EasyCon.Core.Script;
using OpenCvSharp;
using System.Text.Json;

namespace EasyCon.Tests.Maa;

[TestFixture]
public sealed class MaaOcrTests
{
    private static MaaOcrProfile Profile()
    {
        string? path = Environment.GetEnvironmentVariable("EASYCON_TEST_MAA_OCR_PROFILE");
        if (string.IsNullOrWhiteSpace(path))
        {
            if (Environment.GetEnvironmentVariable("EASYCON_MAA_REQUIRE_OCR_TESTS") == "1") Assert.Fail("CI 必须提供 Maa OCR 试点模型 profile");
            Assert.Ignore("未指定 EASYCON_TEST_MAA_OCR_PROFILE；不将缺模型计为 OCR 通过");
        }
        return MaaOcrProfile.Load(path!);
    }

    [Test]
    public void ExplicitImageOcr_UsesRealMaaModel_AndKeepsLegacyFailureAndConfidenceState()
    {
        MaaOcrProfile profile = Profile();
        using MaaOcrService ocr = new(profile);
        using Mat image = new(64, 256, MatType.CV_8UC3, Scalar.All(255));
        Cv2.PutText(image, "12345", new Point(12, 46), HersheyFonts.HersheySimplex, 1.3, Scalar.Black, 2, LineTypes.AntiAlias);
        ImageRef input = ImageRef.FromBase64(Convert.ToBase64String(image.ToBytes(".png")));
        Assert.That(ocr.Init(new OcrConfig { Language = "eng", ModelPath = Path.Combine("old", "Tessdata") }), Is.True);
        string text = ocr.Recognize(input, new OcrQuery { X = 4, Y = 2, Width = 0, Height = -1, Language = "eng" });
        TestContext.Out.WriteLine(JsonSerializer.Serialize(new { truth = "12345", actual = text, confidence = ocr.LastConfidence, rawConfidence = ocr.LastRawConfidence, profile = profile.Digest, synthetic = true }));
        Assert.That(text, Is.EqualTo("12345"));
        int confidence = ocr.LastConfidence;
        Assert.That(confidence, Is.GreaterThan(0));
        Assert.That(ocr.Recognize(input, new OcrQuery { X = 1000, Y = 0 }), Is.EqualTo("OCR ARGS ERR!"));
        Assert.That(ocr.Recognize(ImageRef.FromBase64("invalid"), new OcrQuery()), Is.Empty);
        Assert.That(ocr.LastConfidence, Is.EqualTo(confidence));
        Assert.That(ocr.Init(new OcrConfig { Language = "eng" }), Is.True);
        Assert.That(ocr.LastConfidence, Is.EqualTo(confidence));
        Assert.That(ocr.Init(new OcrConfig { Language = "jpn" }), Is.False, "不能用中英模型冒充日文映射");
        Assert.That(ocr.Init(new OcrConfig { Language = "eng", Options = new Dictionary<string, string> { ["tess:psmode"] = "AUTO" } }), Is.False);
        Assert.That(ocr.LastConfidence, Is.EqualTo(confidence));
        using MaaOcrService next = ocr.CreateInvocation(default);
        Assert.That(next.LastConfidence, Is.Zero, "独立 ECS 调用不能继承上次 OCR_CONF");
        Assert.That(next.ModelGeneration, Is.EqualTo(ocr.ModelGeneration));
        MaaOcrRuntimeCheck check = MaaHostDiagnostics.CheckOcr(profile.SourcePath);
        Assert.That(check.Actual, Is.EqualTo(check.Expected));
        Assert.That(check.ModelGeneration, Is.EqualTo(profile.Digest));
    }

    [Test]
    public void ModelProfile_RejectsUnsupportedAliases_AndCancellationPropagates()
    {
        MaaOcrProfile profile = Profile();
        Assert.Throws<InvalidDataException>(() => profile.Resolve(new OcrConfig { Language = "eng", ModelPath = "other-traineddata" }));
        Assert.Throws<InvalidDataException>(() => profile.Resolve(new OcrConfig { Language = "eng", Options = new Dictionary<string, string> { ["tess:engineMode"] = "LSTM_ONLY" } }));
        Assert.Throws<InvalidDataException>(() => profile.Resolve(new OcrConfig { Language = "eng", Options = new Dictionary<string, string> { ["tess:whitelist"] = "012345" } }));
        using MaaOcrService ocr = new(profile, new CancellationToken(true));
        Assert.Throws<OperationCanceledException>(() => ocr.Recognize(ImageRef.FromBase64("invalid"), new OcrQuery()));
        using MaaOcrService flowOcr = new(profile);
        Assert.Throws<OperationCanceledException>(() => flowOcr.Recognize(ImageRef.FromBase64("invalid"), new OcrQuery(), new CancellationToken(true)));
    }

    [Test]
    public void ExistingEcsOcrSyscall_PassesOcrRegionRulesToMaa_WithoutAnyHardwareModule()
    {
        MaaOcrProfile profile = Profile();
        using Mat image = new(64, 256, MatType.CV_8UC3, Scalar.All(255));
        Cv2.PutText(image, "12345", new Point(12, 46), HersheyFonts.HersheySimplex, 1.3, Scalar.Black, 2, LineTypes.AntiAlias);
        FixedImage capture = new(Convert.ToBase64String(image.ToBytes(".png")));
        Output output = new();
        IScriptSession session = new EasyScriptEngine().FromSource("$text = OCR(4, 2, 0, -1, \"eng\")\nPRINT $text\n$confidence = OCR_CONF()\nPRINT $confidence",
            new ScriptHostOptions { Compile = ScriptCompileProfiles.Interactive([]) });
        Assert.That(session.Info.Diagnostics.Where(d => d.IsError), Is.Empty);
        using CapabilityLease lease = ScriptHostAssembler.Assemble(new ScriptHostContext
        {
            Ocr = new MaaOcrService(profile),
            CaptureSource = capture,
            Console = output,
            EnableInference = false,
        });
        session.Run(default, lease.Capabilities);
        Assert.That(output.Lines[0], Is.EqualTo("12345"));
        Assert.That(int.Parse(output.Lines[1]), Is.GreaterThan(0));
        Assert.That(capture.Requests, Is.EqualTo(new[] { new[] { -1, -1, -1, -1 } }));
        Assert.That(lease.Capabilities.Input, Is.Null);
    }

    [Test]
    public void TextLabels_UseMaaOcrInCallbackContext_WithoutExpectedFiltering()
    {
        MaaOcrProfile profile = Profile();
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "maa-text-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            EasyCon.Capture.ImgLabel label = new()
            {
                searchMethod = EasyCon.Capture.SearchMethod.TesserDetect,
                ImgBase64 = "12345X",
                RangeX = 30,
                RangeY = 20,
                RangeWidth = 256,
                RangeHeight = 64,
            };
            File.WriteAllText(Path.Combine(root, "text.IL"), JsonSerializer.Serialize(label));
            LabelGeneration generation = MaaLabelConverter.Build(LegacyLabelCatalog.Scan([root]), Path.Combine(root, "cache"),
                EasyCon.Capture.LegacyCompatibilityProfile.LegacyGui, ocrProfile: profile).Generation!;
            Assert.That(generation.Labels["text"].NativeParameters, Does.Not.Contain("expected"));
            using Mat image = new(1080, 1920, MatType.CV_8UC3, Scalar.All(255));
            using (Mat roi = new(image, new Rect(30, 20, 256, 64)))
                Cv2.PutText(roi, "12345", new Point(12, 46), HersheyFonts.HersheySimplex, 1.3, Scalar.Black, 2, LineTypes.AntiAlias);
            using MaaVisionService vision = new(generation);
            LegacyRecognitionTrace result = vision.MatchImage("text", image.ToBytes(".png"));
            Assert.That(result.LegacyInteger, Is.InRange(70, 84), "近似文本应保留编辑距离分数，不能被 expected 提前排除");
            Assert.That(result.Route, Is.EqualTo("ocr-profile-pilot"));
            Assert.That(result.Box, Is.EqualTo(new[] { 30, 20, 256, 64 }));
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class FixedImage(string png) : ICaptureSource
    {
        public List<int[]> Requests { get; } = [];
        public string? CaptureFrame(int x, int y, int width, int height) { Requests.Add([x, y, width, height]); return png; }
    }

    private sealed class Output : IConsoleIo
    {
        public List<string> Lines { get; } = [];
        public void Print(string message, bool newline = true) => Lines.Add(message);
        public void Alert(string message) => Lines.Add(message);
    }
}