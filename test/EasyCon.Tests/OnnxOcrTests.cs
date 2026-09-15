using EasyCon.Capture;
using EasyCon.Capture.Ocr;
using EasyCon.Capture.Ocr.Onnx;
using EasyCon.Script;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;

namespace EasyCon.Tests;

[TestFixture]
public sealed class OnnxOcrTests
{
    [TestCase(31, 8, 2048, 32)]
    [TestCase(32, 8, 2048, 32)]
    [TestCase(33, 8, 2048, 40)]
    [TestCase(2047, 8, 2048, 2048)]
    [TestCase(2048, 16, 2047, 2032)]
    public void RecognitionWidthUsesConfiguredAlignment(int width, int multiple, int maximum, int expected)
    {
        Assert.That(OnnxRecognizer.AlignWidth(width, multiple, maximum), Is.EqualTo(expected));
    }

    [Test]
    public void CtcDecoderSupportsUnicodeTokensBlankAndOptionalSpace()
    {
        string[] dictionary = ["A", "文字"];
        var options = new RecOptions { AppendSpaceClass = true };
        float[] scores = Scores(4, (1, .9f), (1, .8f), (0, .95f), (2, .7f), (3, .6f));

        OcrRecognizeResult result = PaddleCtcDecoder.Decode(scores, 5, 4, dictionary, options);

        Assert.Multiple(() =>
        {
            Assert.That(result.Text, Is.EqualTo("A文字 "));
            Assert.That(result.Confidence, Is.EqualTo(.7333333f).Within(.0001f));
        });
    }

    [Test]
    public void CtcDecoderAppliesMinimumScoreWithoutDiscardingConfidence()
    {
        var options = new RecOptions { MinScore = .9f };
        float[] scores = Scores(2, (1, .75f));

        OcrRecognizeResult result = PaddleCtcDecoder.Decode(scores, 1, 2, ["A"], options);

        Assert.Multiple(() =>
        {
            Assert.That(result.Text, Is.Empty);
            Assert.That(result.Confidence, Is.EqualTo(.75f));
        });
    }

    [TestCase("ONNX", GpuBackend.Cpu)]
    [TestCase("onnx:cpu", GpuBackend.Cpu)]
    [TestCase("ONNX:OpenCL", GpuBackend.OpenCL)]
    [TestCase("ONNX:CUDA", GpuBackend.Cuda)]
    public void CacheParsesOnnxBackend(string mode, GpuBackend expected)
    {
        Assert.That(OcrEngineCache.ParseOnnxBackend(mode), Is.EqualTo(expected));
    }

    [TestCase("ONNX:unknown")]
    [TestCase("ONNX:999")]
    public void CacheRejectsUnknownOnnxBackend(string mode)
    {
        Assert.That(() => OcrEngineCache.ParseOnnxBackend(mode),
            Throws.ArgumentException.With.Message.Contains("Unknown ONNX backend"));
    }

    [Test]
    public void FailedReplacementKeepsTheWorkingRecognizer()
    {
        var factory = new StubFactory();
        using var cache = new OcrEngineCache(factory);
        Assert.That(cache.Init("sample", "first", "DEFAULT", "SINGLE_LINE"), Is.True);
        var original = (StubRecognizer)cache.TryGet("sample")!;

        factory.Fail = true;
        Assert.That(cache.Init("sample", "second", "DEFAULT", "SINGLE_LINE"), Is.False);

        Assert.Multiple(() =>
        {
            Assert.That(cache.TryGet("sample"), Is.SameAs(original));
            Assert.That(original.Disposed, Is.False);
            Assert.That(cache.LastError, Does.Contain("requested failure"));
        });
    }

    [Test]
    public void JsonManifestLoadsGenericModelAndPreprocessingOptions()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"onnx-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "sample_ocr.json");
        try
        {
            File.WriteAllText(path,
                """
                {
                  "recognitionModel": "model.onnx",
                  "characterDictionary": "dict.txt",
                  "detection": {
                    "inputMultiple": 16,
                    "mean": [1, 2, 3],
                    "standardDeviation": [0.5, 0.25, 0.125]
                  },
                  "recognition": {
                    "imageHeight": 48,
                    "widthMultiple": 1,
                    "scale": 0.00392156862745098,
                    "mean": [0, 0, 0],
                    "swapRedBlue": true,
                    "blankIndex": 0
                  }
                }
                """);

            OnnxOcrModelConfig config = OnnxOcrModelConfig.Load(path);

            Assert.Multiple(() =>
            {
                Assert.That(config.RecognitionModel, Is.EqualTo("model.onnx"));
                Assert.That(config.CharacterDictionary, Is.EqualTo("dict.txt"));
                Assert.That(config.Detection.InputMultiple, Is.EqualTo(16));
                Assert.That(config.Detection.Mean, Is.EqualTo(new[] { 1d, 2d, 3d }));
                Assert.That(config.Detection.StandardDeviation, Is.EqualTo(new[] { .5d, .25d, .125d }));
                Assert.That(config.Recognition.ImageHeight, Is.EqualTo(48));
                Assert.That(config.Recognition.WidthMultiple, Is.EqualTo(1));
                Assert.That(config.Recognition.Scale, Is.EqualTo(1.0 / 255).Within(1e-12));
                Assert.That(config.Recognition.Mean, Is.EqualTo(new[] { 0d, 0d, 0d }));
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void FourArgumentOcrInitIsAvailableToScripts()
    {
        CompileResult result = Compilation.CompileSource(
            "$ok = OCR_INIT(\"sample\", \"models\", \"ONNX\", \"sample_ocr.json\")",
            new CompileOptions { UseDiskCache = false });

        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty);
    }

    [Test]
    public void OpenCvDnnCanRunConfiguredOnnxRecognizer()
    {
        string? model = Environment.GetEnvironmentVariable("EASYCON_ONNX_TEST_MODEL");
        string? dictionary = Environment.GetEnvironmentVariable("EASYCON_ONNX_TEST_DICTIONARY");
        if (string.IsNullOrWhiteSpace(model) || string.IsNullOrWhiteSpace(dictionary))
            Assert.Ignore("Set EASYCON_ONNX_TEST_MODEL and EASYCON_ONNX_TEST_DICTIONARY for integration coverage.");

        byte[] image = CreateTextImage("OPEN CV");
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"onnx-live-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string configPath = Path.Combine(directory, "sample_ocr.json");
        File.WriteAllText(configPath, JsonSerializer.Serialize(new OnnxOcrModelConfig
        {
            RecognitionModel = model!,
            CharacterDictionary = dictionary!,
            Recognition = new RecOptions
            {
                ImageHeight = 48,
                WidthMultiple = 1,
                Scale = 1.0 / 255,
                Mean = [0, 0, 0],
            },
        }));

        try
        {
            using var cache = new OcrEngineCache();
            Assert.That(cache.Init("sample", directory, "ONNX", "sample_ocr.json"), Is.True, cache.LastError);
            OcrRecognizeResult result = cache.TryGet("sample")!.Recognize(image);

            Assert.Multiple(() =>
            {
                Assert.That(result.Text, Is.Not.Empty);
                Assert.That(float.IsFinite(result.Confidence), Is.True);
                Assert.That(result.Confidence, Is.InRange(0, 1));
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static float[] Scores(int classCount, params (int Index, float Confidence)[] steps)
    {
        float[] scores = new float[classCount * steps.Length];
        for (int t = 0; t < steps.Length; t++)
        {
            for (int c = 0; c < classCount; c++) scores[t * classCount + c] = .01f;
            scores[t * classCount + steps[t].Index] = steps[t].Confidence;
        }
        return scores;
    }

    private static byte[] CreateTextImage(string text)
    {
        using var bitmap = new Bitmap(320, 48);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.White);
        using var font = new Font(FontFamily.GenericSansSerif, 26, FontStyle.Regular, GraphicsUnit.Pixel);
        graphics.DrawString(text, font, Brushes.Black, 4, 8);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private sealed class StubFactory : IOcrEngineFactory
    {
        public bool Fail { get; set; }

        public IOcrRecognizer CreateRecognizer(string lang, string dataPath, string engineMode, string psmode)
        {
            if (Fail) throw new InvalidOperationException("requested failure");
            return new StubRecognizer();
        }

        public IOcrEngine? CreateEngine(string lang, string dataPath, string engineMode, string psmode) => null;
    }

    private sealed class StubRecognizer : IOcrRecognizer
    {
        public bool Disposed { get; private set; }

        public OcrRecognizeResult Recognize(byte[] image) => new("stub", 1);

        public void Dispose() => Disposed = true;
    }
}
