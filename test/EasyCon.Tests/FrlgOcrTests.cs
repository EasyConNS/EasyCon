using EasyCon.Capture;
using EasyCon.Capture.Ocr;
using EasyCon.Capture.Ocr.Frlg;
using EasyCon.Core;
using EzCv;
using System.Text.Json;

namespace EasyCon.Tests;

[TestFixture]
public sealed class FrlgOcrTests
{
    private FrlgTextReader? _textReader;

    private static Mat Fixture(string name) => Mat.FromImageData(File.ReadAllBytes(
        Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "Frlg", name)));

    [OneTimeSetUp]
    public void SetUpTextModels()
    {
        string directory = Environment.GetEnvironmentVariable("EASYCON_FRLG_MODELS")
            ?? Path.Combine(TestContext.CurrentContext.TestDirectory, "models", "frlg");
        if (File.Exists(Path.Combine(directory, "chinese", "rec.onnx"))
            && File.Exists(Path.Combine(directory, "tessdata", "jpn.traineddata")))
            _textReader = new FrlgTextReader(directory);
    }

    [OneTimeTearDown]
    public void TearDownTextModels() => _textReader?.Dispose();

    [TestCase("nyash_jpn_45345.png", FrlgOcr.JapaneseTid, "45345")]
    [TestCase("tom_eng_60895.jpg", FrlgOcr.EnglishTid, "60895")]
    public void TidReadsThroughExistingOcrDelegate(string file, string scene, string expected)
    {
        using Mat frame = Fixture(file);
        using OcrEngineCache cache = new(new ForbiddenFactory());
        EasyScript.OcrDelegate read = OcrDelegateFactory.Create((Func<Mat>)(() => frame.Clone()), cache);
        Rect region = FrlgOcr.DefaultRegion(scene, frame.Width, frame.Height);

        Assert.That(read(region.X, region.Y, region.Width, region.Height, scene), Is.EqualTo(expected));
        Assert.That(cache.LastFrlgResult?.Success, Is.True);
        Assert.That(cache.LastConfidence, Is.GreaterThan(0));
    }

    [TestCase("FRLG_JPN_LEVEL", "30")]
    public void SummaryPageOneDigitsReadWithoutTextModels(string scene, string expected)
    {
        using Mat frame = Fixture("Page1/deoxys_1_jpn.png");
        Rect region = FrlgOcr.DefaultRegion(scene, frame.Width, frame.Height);
        FrlgReadResult result = FrlgOcr.ReadFrame(frame, region, scene);

        Assert.That(result.Text, Is.EqualTo(expected), result.Failure);
    }

    [TestCase("FRLG_JPN_HP", "70")]
    [TestCase("FRLG_JPN_ATTACK", "119")]
    [TestCase("FRLG_JPN_DEFENSE", "21")]
    [TestCase("FRLG_JPN_SP_ATTACK", "119")]
    [TestCase("FRLG_JPN_SP_DEFENSE", "23")]
    [TestCase("FRLG_JPN_SPEED", "107")]
    public void SummaryPageTwoStatsReadWithoutTextModels(string scene, string expected)
    {
        using Mat frame = Fixture("Page2/deoxys_1_jpn.png");
        Rect region = FrlgOcr.DefaultRegion(scene, frame.Width, frame.Height);
        FrlgReadResult result = FrlgOcr.ReadFrame(frame, region, scene);

        Assert.That(result.Text, Is.EqualTo(expected), result.Failure);
    }

    [TestCase("Page1/bulbasaur_1_jpn.png", "FRLG_JPN_SUMMARY_NAME", "フシギダネ")]
    [TestCase("Page1/bulbasaur_1_jpn.png", "FRLG_JPN_NATURE", "のうてんき")]
    [TestCase("Page1/deoxys_1_jpn.png", "FRLG_JPN_SUMMARY_NAME", "デオキシス")]
    [TestCase("Page1/deoxys_1_jpn.png", "FRLG_JPN_NATURE", "しんちょう")]
    [TestCase("Wild/eng_dragonair.jpg", "FRLG_JPN_WILD_LEVEL", "28")]
    public void TextScenesReadWithPinnedModels(string file, string scene, string expected)
    {
        if (_textReader == null)
            Assert.Ignore("Pinned FRLG text models are optional; run tools/FrlgOcr/fetch_resources.py to enable this test.");

        using Mat frame = Fixture(file);
        Rect region = scene == "FRLG_JPN_WILD_LEVEL"
            ? new Rect(755, 129, 70, 66)
            : FrlgOcr.DefaultRegion(scene, frame.Width, frame.Height);
        FrlgReadResult result = FrlgOcr.ReadFrame(frame, region, scene, textReader: _textReader);

        Assert.That(result.Text, Is.EqualTo(expected),
            () => JsonSerializer.Serialize(result));
    }

    [Test]
    public void FrlgLabelUsesSceneInsteadOfTargetImage()
    {
        using Mat frame = Fixture("nyash_jpn_45345.png");
        Rect region = FrlgOcr.DefaultRegion(FrlgOcr.JapaneseTid, frame.Width, frame.Height);
        ImgLabel label = new()
        {
            name = "FRLG TID",
            searchMethod = SearchMethod.FrlgOcr,
            OcrScene = FrlgOcr.JapaneseTid,
            RangeX = region.X,
            RangeY = region.Y,
            RangeWidth = region.Width,
            RangeHeight = region.Height,
        };

        List<System.Drawing.Point> matches = label.Search(frame, out double quality, string.Empty);

        Assert.That(label.Valid(), Is.True);
        Assert.That(matches, Has.Count.EqualTo(1));
        Assert.That(label.LastOcrText, Is.EqualTo("45345"));
        Assert.That(quality, Is.GreaterThan(0));
    }

    [Test]
    public void FrlgLabelPersistsSceneButNotLastResult()
    {
        ImgLabel label = new()
        {
            searchMethod = SearchMethod.FrlgOcr,
            OcrScene = "FRLG_JPN_NAME",
            RangeWidth = 100,
            RangeHeight = 50,
        };

        string json = JsonSerializer.Serialize(label);

        Assert.That(json, Does.Contain("FRLG_JPN_NAME"));
        Assert.That(json, Does.Not.Contain(nameof(ImgLabel.LastOcrText)));
        Assert.That(json, Does.Not.Contain(nameof(ImgLabel.LastOcrFailure)));
    }

    [Test]
    public void DictionariesCoverAllNaturesAndDistinguishSimilarNames()
    {
        Assert.That(FrlgJapaneseLexicon.NameCount, Is.GreaterThan(1000));
        Assert.That(FrlgJapaneseLexicon.NatureCount, Is.EqualTo(25));
        Assert.That(FrlgJapaneseLexicon.Match("ハクリュー", false).Text, Is.EqualTo("ハクリュー"));
        Assert.That(FrlgJapaneseLexicon.Match("カイリュー", false).Text, Is.EqualTo("カイリュー"));
        Assert.That(FrlgJapaneseLexicon.Match("サンター", false).Text, Is.EqualTo("サンダー"));
        Assert.That(FrlgJapaneseLexicon.Match("ラブラス", false).Text, Is.EqualTo("ラプラス"));
        Assert.That(FrlgJapaneseLexicon.Match("ハクリュー", false, ["dragonite"]).Accepted, Is.False);
        Assert.That(FrlgJapaneseLexicon.Match("ハクリュー", false, ["dragonair"]).Accepted, Is.True);
    }

    [TestCase("Wild/eng_dragonair.jpg", '♂')]
    [TestCase("Wild/eng_chansey.jpg", '♀')]
    public void BattleGenderMarkerComesFromTheCapturedGlyph(string file, char expected)
    {
        using Mat frame = Fixture(file);
        using Mat region = new(frame, new Rect(300, 122, 350, 72));

        Assert.That(FrlgTextReader.DetectGenderMarker(region), Is.EqualTo(expected));
    }

    [Test]
    public void ExistingOcrLanguagesStillUseOriginalRecognizer()
    {
        using Mat frame = Fixture("nyash_jpn_45345.png");
        RecordingFactory factory = new();
        using OcrEngineCache cache = new(factory);
        EasyScript.OcrDelegate read = OcrDelegateFactory.Create((Func<Mat>)(() => frame.Clone()), cache);

        Assert.That(read(1, 1, 100, 50, "jpn"), Is.EqualTo("legacy-text"));
        Assert.That(factory.Language, Is.EqualTo("jpn"));
        Assert.That(cache.LastFrlgResult, Is.Null);
    }

    private sealed class ForbiddenFactory : IOcrEngineFactory
    {
        public IOcrRecognizer CreateRecognizer(string lang, string dataPath, string engineMode, string psmode)
            => throw new InvalidOperationException("Digit scenes must not initialize text OCR.");

        public IOcrEngine? CreateEngine(string lang, string dataPath, string engineMode, string psmode) => null;
    }

    private sealed class RecordingFactory : IOcrEngineFactory
    {
        public string? Language { get; private set; }

        public IOcrRecognizer CreateRecognizer(string lang, string dataPath, string engineMode, string psmode)
        {
            Language = lang;
            return new FakeRecognizer();
        }

        public IOcrEngine? CreateEngine(string lang, string dataPath, string engineMode, string psmode) => null;
    }

    private sealed class FakeRecognizer : IOcrRecognizer
    {
        public OcrRecognizeResult Recognize(byte[] image) => new("legacy-text", .9f);
        public void Dispose() { }
    }
}