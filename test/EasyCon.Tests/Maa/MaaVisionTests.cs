using EasyCon.Capture;
using EasyCon.Core.Hosting;
using EasyCon.Core.Maa;
using EasyCon.Core.Script;
using EasyDevice;
using EasyDevice.Connection;
using OpenCvSharp;
using System.Collections.Concurrent;
using System.Text.Json;

namespace EasyCon.Tests.Maa;

[TestFixture]
public sealed partial class MaaVisionTests
{
    private string _root = "";

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "maa-vision-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "ImgLabel"));
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, true);

    private static Mat Pattern(bool inverse = false, bool alpha = false)
    {
        Mat image = new(12, 16, alpha ? MatType.CV_8UC4 : MatType.CV_8UC3);
        Random random = new(71);
        for (int y = 0; y < 12; y++)
            for (int x = 0; x < 16; x++)
            {
                byte value = (byte)random.Next(20, 230);
                if (inverse) value = (byte)(255 - value);
                if (alpha) image.Set(y, x, new Vec4b(value, value, value, (byte)(x % 3 == 0 ? 0 : 255)));
                else image.Set(y, x, new Vec3b(value, value, value));
            }
        return image;
    }

    private string SaveLabel(SearchMethod method, Mat template, string name = "sample")
    {
        ImgLabel label = new()
        {
            searchMethod = method,
            ImgBase64 = Convert.ToBase64String(template.ToBytes(".png")),
            RangeX = 30,
            RangeY = 20,
            RangeWidth = 16,
            RangeHeight = 12,
            TargetWidth = 16,
            TargetHeight = 12,
        };
        string path = Path.Combine(_root, "ImgLabel", name + ".IL");
        File.WriteAllText(path, JsonSerializer.Serialize(label));
        return path;
    }

    private LabelGeneration ConvertLabels(LegacyCompatibilityProfile profile = LegacyCompatibilityProfile.LegacyGui)
        => MaaLabelConverter.Build(LegacyLabelCatalog.FromParents(_root), Path.Combine(_root, "cache"), profile).Generation!;

    [TestCase(SearchMethod.CCoeffNormed, false)]
    [TestCase(SearchMethod.CCoeffNormed, true)]
    [TestCase(SearchMethod.CCorrNormed, false)]
    [TestCase(SearchMethod.SqDiffNormed, false)]
    [TestCase(SearchMethod.MaskedSqDiffNormed, false)]
    [TestCase(SearchMethod.EdgeDetectXY, false)]
    [TestCase(SearchMethod.EdgeDetectLaplacian, false)]
    public void NativeAndCustomRecognition_PreserveLegacyIntegersAndGlobalBoxes(SearchMethod method, bool inverse)
    {
        using Mat template = Pattern(alpha: method == SearchMethod.MaskedSqDiffNormed);
        string path = SaveLabel(method, template);
        using Mat frame = new(1080, 1920, MatType.CV_8UC3, Scalar.All(71));
        using Mat content = Pattern(inverse);
        using (Mat target = new(frame, new Rect(30, 20, 16, 12))) content.CopyTo(target);
        ImgLabel label = ImgLabel.Load(path);
        label.Search(frame, out double expected, "");
        foreach (LegacyCompatibilityProfile profile in Enum.GetValues<LegacyCompatibilityProfile>())
        {
            using MaaVisionService service = new(ConvertLabels(profile));
            LegacyRecognitionTrace actual = service.MatchImage("sample", frame.ToBytes(".png"), 17, 3);
            Assert.Multiple(() =>
            {
                Assert.That(actual.LegacyInteger, Is.EqualTo(profile == LegacyCompatibilityProfile.LegacyCli ? (int)Math.Ceiling(expected) : (int)expected));
                Assert.That(actual.LegacyFloat, Is.EqualTo(expected));
                Assert.That(actual.Box, Is.EqualTo(new[] { 30, 20, 16, 12 }));
                Assert.That(actual.FrameId, Is.EqualTo(17));
                Assert.That(actual.SourceEpoch, Is.EqualTo(3));
                Assert.That(service.Session.RetainedResults, Is.Zero);
            });
            if (inverse) Assert.That(actual.Route, Is.EqualTo("native-no-candidate"));
        }
    }

    [Test]
    public void Adapter_PreservesNegativeScoresAndBothRoundingProfiles_RejectsNonfinite()
    {
        RawMatchResult raw = new(1.2345, default, SearchMethod.MaskedSqDiffNormed);
        Assert.That(LegacyAlgorithmAdapter.ToLegacyFloat(raw), Is.LessThan(0));
        Assert.That(LegacyAlgorithmAdapter.ToLegacyInteger(raw, LegacyCompatibilityProfile.LegacyGui), Is.EqualTo(-23));
        Assert.That(LegacyAlgorithmAdapter.ToLegacyInteger(raw, LegacyCompatibilityProfile.LegacyCli), Is.EqualTo(-23));
        RawMatchResult positive = new(0.12345, default, SearchMethod.CCorrNormed);
        Assert.That(LegacyAlgorithmAdapter.ToLegacyInteger(positive, LegacyCompatibilityProfile.LegacyGui), Is.EqualTo(12));
        Assert.That(LegacyAlgorithmAdapter.ToLegacyInteger(positive, LegacyCompatibilityProfile.LegacyCli), Is.EqualTo(13));
        Assert.Throws<InvalidDataException>(() => LegacyAlgorithmAdapter.ToLegacyFloat(raw with { Score = double.NaN }));
        Assert.Throws<InvalidDataException>(() => LegacyAlgorithmAdapter.ToLegacyInteger(raw, (LegacyCompatibilityProfile)99));
        RawMatchResult belowBoundary = new(0.129999999999, default, SearchMethod.CCorrNormed);
        Assert.That(LegacyAlgorithmAdapter.ToLegacyInteger(belowBoundary, LegacyCompatibilityProfile.LegacyGui), Is.EqualTo(12));
        Assert.That(LegacyAlgorithmAdapter.ToLegacyInteger(belowBoundary, LegacyCompatibilityProfile.LegacyCli), Is.EqualTo(13));
    }

    [Test]
    public void EqualTemplateHits_KeepLegacyFirstBox_AndApplyRoiOffsetOnce()
    {
        using Mat template = Pattern();
        string path = SaveLabel(SearchMethod.CCoeffNormed, template);
        ImgLabel label = ImgLabel.Load(path);
        label.RangeWidth = 40;
        File.WriteAllText(path, JsonSerializer.Serialize(label));
        using Mat image = new(1080, 1920, MatType.CV_8UC3, Scalar.All(71));
        using (Mat first = new(image, new Rect(30, 20, 16, 12))) template.CopyTo(first);
        using (Mat second = new(image, new Rect(54, 20, 16, 12))) template.CopyTo(second);
        System.Drawing.Point expected = label.Search(image, out double score, "")[0];
        using MaaVisionService vision = new(ConvertLabels());
        LegacyRecognitionTrace actual = vision.MatchImage("sample", image.ToBytes(".png"));
        Assert.That(actual.LegacyFloat, Is.EqualTo(score));
        Assert.That(actual.Box, Is.EqualTo(new[] { 30 + expected.X, 20 + expected.Y, 16, 12 }));
    }

    [Test]
    public void Conversion_IsDeterministicAndTransactional_AndPreservesInvalidNames()
    {
        using Mat template = Pattern();
        string path = SaveLabel(SearchMethod.CCoeffNormed, template);
        string original = File.ReadAllText(path);
        LabelGeneration first = ConvertLabels();
        LabelGeneration repeated = ConvertLabels();
        Assert.That(first.Id, Is.EqualTo(repeated.Id));
        Assert.That(File.ReadAllText(path), Is.EqualTo(original));
        string current = File.ReadAllText(Path.Combine(_root, "cache", "current.json"));
        File.WriteAllText(Path.Combine(_root, "ImgLabel", "broken.IL"), "{}");
        LabelDiscovery discovery = LegacyLabelCatalog.FromParents(_root);
        LabelConversionResult strict = MaaLabelConverter.Build(discovery, Path.Combine(_root, "cache"), LegacyCompatibilityProfile.LegacyGui, strict: true);
        Assert.That(strict.Published, Is.False);
        Assert.That(strict.Report.ErrorCount, Is.EqualTo(1));
        Assert.That(File.ReadAllText(Path.Combine(_root, "cache", "current.json")), Is.EqualTo(current));
        LabelGeneration lenient = ConvertLabels();
        Assert.Throws<InvalidDataException>(() => lenient.Preflight(["broken"]));
        Assert.DoesNotThrow(() => lenient.Preflight(["sample"]));
    }

    [Test]
    public void Discovery_BrokenEarlierFileDoesNotOccupyName_AndReportsLaterDuplicates()
    {
        using Mat template = Pattern();
        string valid = SaveLabel(SearchMethod.CCoeffNormed, template);
        string earlier = Path.Combine(_root, "earlier");
        Directory.CreateDirectory(earlier);
        File.WriteAllText(Path.Combine(earlier, "sample.IL"), "invalid json");
        LabelDiscovery discovery = LegacyLabelCatalog.Scan([earlier, Path.GetDirectoryName(valid)!, Path.GetDirectoryName(valid)!]);
        Assert.That(discovery.Selected.Single().Path, Is.EqualTo(valid));
        Assert.That(discovery.Diagnostics.Select(item => item.Code), Is.EquivalentTo(new[] { "parse-error", "shadowed" }));
    }

    [Test]
    public void Session_ClearsHistoryAfterEveryRequest_AndCancellationDoesNotOpenHardware()
    {
        using Mat template = Pattern();
        SaveLabel(SearchMethod.CCoeffNormed, template);
        using Mat frame = new(1080, 1920, MatType.CV_8UC3, Scalar.All(71));
        using (Mat roi = new(frame, new Rect(30, 20, 16, 12))) template.CopyTo(roi);
        using MaaVisionService service = new(ConvertLabels());
        byte[] png = frame.ToBytes(".png");
        for (int index = 0; index < 20; index++) service.MatchImage("sample", png);
        Assert.That(service.Session.CompletedRequests, Is.EqualTo(40));
        Assert.That(service.Session.RetainedResults, Is.Zero);
        Assert.That(service.Session.LiveImages, Is.Zero);
        Assert.That(service.Session.QueuedRequests, Is.Zero);
        Assert.CatchAsync<OperationCanceledException>(async () => await service.Session.RecognizeAsync(
            new("DirectHit", "{}", png, "cancelled", null, null), new CancellationToken(true)));
    }

    [Test]
    public async Task QueryHit_IsValidComputation_WhileExplicitConditionEvaluatesLegacyThreshold()
    {
        using Mat template = Pattern();
        SaveLabel(SearchMethod.CCoeffNormed, template);
        LabelGeneration generation = ConvertLabels();
        using Mat frame = new(1080, 1920, MatType.CV_8UC3, Scalar.All(71));
        using Mat inverse = Pattern(inverse: true);
        using (Mat roi = new(frame, new Rect(30, 20, 16, 12))) inverse.CopyTo(roi);
        byte[] png = frame.ToBytes(".png");
        MaaLabelEntry entry = generation.Labels["sample"];
        using MaaRecognitionSession session = new(generation.Directory, generation.Labels);
        MaaRecognitionResult query = await session.RecognizeAsync(new(entry.CompatType, entry.CompatParameters, png, "query", null, generation.Id), default);
        MaaRecognitionResult rejected = await session.RecognizeAsync(new("Custom",
            MaaLabelConverter.ScoreCondition(entry, ">", 90, generation.Profile), png, "condition-false", null, generation.Id), default);
        MaaRecognitionResult accepted = await session.RecognizeAsync(new("Custom",
            MaaLabelConverter.ScoreCondition(entry, "<=", 90, generation.Profile), png, "condition-true", null, generation.Id), default);
        Assert.That(query.Hit, Is.True);
        Assert.That(rejected.Hit, Is.False);
        Assert.That(accepted.Hit, Is.True);
        Assert.Throws<InvalidDataException>(() => MaaLabelConverter.ScoreCondition(entry, "contains", 90, generation.Profile));
    }

    [Test]
    public void MaskWithOpaqueGreenAndPartialAlpha_UsesExactCompatibility_TransparentMaskIsInvalid()
    {
        using Mat template = Pattern(alpha: true);
        template.Set(0, 1, new Vec4b(0, 255, 0, 255));
        template.Set(0, 2, new Vec4b(99, 73, 121, 127));
        string path = SaveLabel(SearchMethod.MaskedSqDiffNormed, template);
        LabelGeneration generation = ConvertLabels();
        Assert.That(generation.Labels["sample"].NativeNode, Is.Null);
        Assert.That(generation.Report.Diagnostics.Select(diagnostic => diagnostic.Code), Does.Contain("green-mask-collision"));
        using Mat frame = new(1080, 1920, MatType.CV_8UC3, Scalar.All(71));
        ImgLabel.Load(path).Search(frame, out double expected, "");
        using MaaVisionService vision = new(generation);
        Assert.That(vision.MatchImage("sample", frame.ToBytes(".png")).LegacyFloat, Is.EqualTo(expected));
        using Mat transparent = new(12, 16, MatType.CV_8UC4, Scalar.All(0));
        SaveLabel(SearchMethod.MaskedSqDiffNormed, transparent, "transparent");
        Assert.That(ConvertLabels().Labels["transparent"].Error, Does.Contain("完全透明"));
    }

    [Test]
    public void EditedLabels_KeepActiveGeneration_AndCorruptCacheDoesNotReplaceCurrent()
    {
        using Mat template = Pattern();
        SaveLabel(SearchMethod.CCoeffNormed, template);
        LabelGeneration original = ConvertLabels();
        int initialActive = MaaVisionService.ActiveResourceVersions;
        using (MaaVisionService active = new(original))
        {
            for (int revision = 0; revision < 10; revision++)
            {
                template.Set(0, 0, new Vec3b((byte)revision, 73, 121));
                SaveLabel(SearchMethod.CCoeffNormed, template);
                LabelGeneration changed = ConvertLabels();
                Assert.That(changed.Id, Is.Not.EqualTo(original.Id));
                Assert.That(Directory.Exists(original.Directory), Is.True);
            }
            Assert.That(MaaVisionService.ActiveResourceVersions, Is.EqualTo(initialActive + 1));
        }
        LabelGeneration current = ConvertLabels();
        Assert.That(MaaVisionService.ActiveResourceVersions, Is.EqualTo(initialActive));
        Assert.That(Directory.Exists(original.Directory), Is.False);
        Assert.That(Directory.GetDirectories(Path.Combine(_root, "cache")).Length, Is.LessThanOrEqualTo(8));
        string pointer = File.ReadAllText(Path.Combine(_root, "cache", "current.json"));
        File.WriteAllText(Path.Combine(current.Directory, "easycon", "labels.index.json"), "{}");
        Assert.Throws<InvalidDataException>(() => ConvertLabels());
        Assert.That(File.ReadAllText(Path.Combine(_root, "cache", "current.json")), Is.EqualTo(pointer));
    }

    [Test]
    public async Task Stop_DrainsConcurrentRecognitionAndRejectsNewRequests()
    {
        using Mat image = new(64, 64, MatType.CV_8UC3, Scalar.All(71));
        byte[] png = image.ToBytes(".png");
        using MaaRecognitionSession session = new();
        Task<MaaRecognitionResult>[] pending = Enumerable.Range(0, 16).Select(index => session.RecognizeAsync(
            new("DirectHit", "{}", png, index.ToString(), null, null), default)).ToArray();
        await session.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        foreach (Task<MaaRecognitionResult> task in pending)
            try { await task; } catch (OperationCanceledException) { }
        Assert.That(pending.All(task => task.IsCompleted), Is.True);
        Assert.That(session.QueuedRequests, Is.Zero);
        Assert.That(session.RetainedResults, Is.Zero);
        Assert.CatchAsync<OperationCanceledException>(async () => await session.RecognizeAsync(
            new("DirectHit", "{}", png, "after-stop", null, null), default));
    }

    [TestCase(false, SwitchButton.A)]
    [TestCase(true, SwitchButton.B)]
    public async Task ExistingEcsBranch_UsesMaaScoreAndWritesExpectedHid(bool inverse, SwitchButton expectedButton)
    {
        using Mat template = Pattern();
        SaveLabel(SearchMethod.CCoeffNormed, template);
        using Mat image = new(1080, 1920, MatType.CV_8UC3, Scalar.All(71));
        using Mat content = Pattern(inverse);
        using (Mat roi = new(image, new Rect(30, 20, 16, 12))) content.CopyTo(roi);
        LabelGeneration generation = ConvertLabels();
        const string code = "IF @sample > 90\n    A 80\nELSE\n    B 80\nENDIF";
        IScriptSession script = new EasyScriptEngine().FromSource(code,
            new ScriptHostOptions { Compile = ScriptCompileProfiles.Interactive(["sample"]) });
        Assert.That(script.Info.Diagnostics.Where(diagnostic => diagnostic.IsError), Is.Empty);
        Assert.That(ScriptHardwareRequirements.RequiresLiveCapture(script.Info), Is.True);
        Assert.That(ScriptHardwareRequirements.ReferencedLabels(script.Info), Is.EqualTo(new[] { "sample" }));
        HidConnection connection = new();
        using MaaControllerSession controller = new(new HidDevice(connection));
        using FrameHub frames = new();
        frames.Connect(new StaticFrameSource(image), "p2-fixture");
        using FrameLease initial = await frames.WaitForFrameAfterAsync(0, 0, new CancellationTokenSource(5000).Token);
        Assert.That(controller.TryConnect("fake:p2"), Is.EqualTo(ControllerConnectResult.Success));
        HostRunCoordinator coordinator = new(controller, frames);
        coordinator.Run("ecs", "unchanged-script.ecs", true, default, scope =>
        {
            using MaaVisionService vision = new(generation, () => scope.AcquireFrame(frames.AcquireLatest), scope.Token);
            using CapabilityLease capabilities = ScriptHostAssembler.Assemble(new ScriptHostContext
            {
                PadInput = scope.CreatePad(),
                VisionService = vision,
                EnableOcr = false,
                EnableInference = false,
            });
            script.Run(scope.Token, capabilities.Capabilities);
            Assert.That(vision.LastTrace!.SourceEpoch, Is.EqualTo(initial.Metadata.SourceEpoch));
        }, requiresCapture: true, compatibilityProfile: generation.Profile.ToString(), resourceGeneration: generation.Id);
        byte[] expected = new SwitchReport { Button = (ushort)expectedButton }.GetBytes();
        Assert.That(connection.Writes.Any(bytes => bytes.SequenceEqual(expected)), Is.True, "断言真实设备写队列的最终 HID 字节");
        Assert.That(connection.Writes.Last(), Is.EqualTo(new SwitchReport().GetBytes()));
        Assert.That(coordinator.LastResult!.BusinessStatus, Is.EqualTo("Succeeded"));
        Assert.That(coordinator.LastResult.ResourceGeneration, Is.EqualTo(generation.Id));
        TestContext.Out.WriteLine(JsonSerializer.Serialize(new
        {
            code,
            inverse,
            expected = Convert.ToHexString(expected),
            actual = connection.Writes.Select(Convert.ToHexString),
            run = coordinator.LastResult
        }));
    }

    private sealed class StaticFrameSource(Mat image) : IFrameSource
    {
        public bool IsOpened => true;
        public Mat GetMatFrame() { Thread.Sleep(15); return image.Clone(); }
        public void SetProperties(int width, int height) { }
        public void Dispose() { }
    }

    private sealed class HidDevice(HidConnection connection) : NintendoSwitch
    {
        protected override IConnection CreateConnection(string connStr, int baudrate) => connection;
    }

    private sealed class HidConnection : IConnection
    {
        public ConcurrentQueue<byte[]> Writes { get; } = new();
        public override event BytesTransferedHandler BytesSent = delegate { };
        public override event StatusChangedHandler StatusChanged = delegate { };
        public override Status CurrentStatus { get; protected set; } = Status.Connecting;
        public override void Connect() { CurrentStatus = Status.Connected; StatusChanged(CurrentStatus); }
        public override void Disconnect() { CurrentStatus = Status.Connecting; }
        public override void ClearQueue() { }
        public override void Write(params byte[] bytes) { Writes.Enqueue(bytes.ToArray()); BytesSent("fake", bytes); }
    }
}