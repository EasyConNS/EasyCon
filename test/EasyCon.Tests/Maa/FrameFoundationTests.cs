using EasyCon.Capture;
using EasyCon.Core;
using EasyCon.Core.Config;
using EasyCon.Core.Maa;
using OpenCvSharp;
using System.Text.Json;

namespace EasyCon.Tests.Maa;

[TestFixture]
public sealed class FrameFoundationTests
{
    private sealed class BlockedSource : IFrameSource
    {
        public readonly ManualResetEventSlim Reading = new();
        public readonly ManualResetEventSlim Return = new();
        public bool Disposed;
        public bool IsOpened => true;
        public Mat GetMatFrame()
        {
            Reading.Set();
            Return.Wait();
            return new Mat(36, 64, MatType.CV_8UC3, Scalar.All(43));
        }
        public void SetProperties(int width, int height) { }
        public void Dispose() => Disposed = true;
    }

    [Test]
    public void LateRead_CannotPublishOrRestart_AndNativeOwnerLivesUntilReadReturns()
    {
        BlockedSource source = new();
        FrameStore store = new();
        FrameProducer producer = new(source, store);
        producer.Start();
        Assert.That(source.Reading.Wait(3000), Is.True);
        Assert.That(producer.Stop(TimeSpan.FromMilliseconds(10)), Is.False);
        Assert.Throws<InvalidOperationException>(producer.Start);
        Assert.That(source.Disposed, Is.False);
        source.Return.Set();
        Assert.That(producer.Stop(TimeSpan.FromSeconds(3)), Is.True);
        Assert.That(store.FrameCount, Is.Zero, "迟到的旧读取不能发布");
        producer.Dispose();
        Assert.That(source.Disposed, Is.True);
    }

    [Test]
    public async Task FrameSlot_PixelsMetadataAndPngAreOnePublication_LeaseSurvivesSourceRetirement()
    {
        FrameStore store = new();
        long epoch = store.BeginSource();
        Mat pixels = new(1080, 1920, MatType.CV_8UC3, new Scalar(1, 73, 211));
        FrameMetadata metadata = FrameMetadata.FromImage(pixels) with { SourceId = "A", SourceEpoch = epoch };
        store.Publish(pixels, metadata);
        using FrameLease frame = store.AcquireLatest()!;
        ReadOnlyMemory<byte> png = frame.EncodedPng;
        Assert.That(frame.EncodedPng.Equals(png), Is.True, "每帧只缓存一份 PNG");
        store.Publish(new Mat(1080, 1920, MatType.CV_8UC3, Scalar.All(99)), metadata with { SourceId = "B" });
        using FrameLease next = await store.WaitForFrameAfterAsync(frame.Metadata.FrameId, 0, default);
        Assert.That(next.Metadata.FrameId, Is.GreaterThan(frame.Metadata.FrameId));
        Assert.That(frame.Metadata.SourceId, Is.EqualTo("A"));
        store.ReleaseCurrent();
        using Mat decoded = Cv2.ImDecode(png.ToArray(), ImreadModes.Color);
        Assert.That(Cv2.Norm(decoded, frame.Mat), Is.Zero);
        string? full = FrameDelegateFactory.CropToBase64(frame.Mat, -1, -1, -1, -1);
        using Mat ecs = Cv2.ImDecode(Convert.FromBase64String(full!), ImreadModes.Color);
        Assert.That(Cv2.Norm(ecs, frame.Mat), Is.Zero);
        Assert.That(FrameDelegateFactory.CropToBase64(frame.Mat, 0, 0, 0, 0), Is.Null);
    }

    [Test]
    public void Normalization_HandlesBgraStrideAndGray_RejectsImplicitStretch()
    {
        using Mat backing = new(40, 68, MatType.CV_8UC4, new Scalar(4, 67, 201, 12));
        using Mat strided = new(backing, new Rect(2, 2, 64, 36));
        using Mat normalized = new FrameNormalization().Apply(strided);
        Assert.That(normalized.Type(), Is.EqualTo(MatType.CV_8UC3));
        Assert.That(normalized.At<Vec3b>(12, 12), Is.EqualTo(new Vec3b(4, 67, 201)));
        using Mat gray = new(36, 64, MatType.CV_8UC1, Scalar.All(77));
        using Mat grayBgr = new FrameNormalization().Apply(gray);
        Assert.That(grayBgr.At<Vec3b>(0, 0), Is.EqualTo(new Vec3b(77, 77, 77)));
        using Mat square = new(100, 100, MatType.CV_8UC3, Scalar.All(0));
        Assert.Throws<ArgumentException>(() => new FrameNormalization().Apply(square));
        using Mat cropped = new FrameNormalization(new Rect(0, 0, 80, 45)).Apply(square);
        Assert.That(cropped.Size(), Is.EqualTo(new Size(1920, 1080)));
        using Mat portrait = new(64, 36, MatType.CV_8UC3, Scalar.All(18));
        using Mat rotated = new FrameNormalization(Rotation: 90).Apply(portrait);
        Assert.That(rotated.Size(), Is.EqualTo(new Size(1920, 1080)));
    }

    [Test]
    public void SourceCatalog_DuplicatesAreSelectable_StableIdsResolveReordering_UnknownNeverFallsBack()
    {
        IReadOnlyList<CaptureSourceDescriptor> first = CaptureSourceCatalog.Describe(
            [("same name", "stable-A", 0), ("same name", "stable-B", 1)], VideoCaptureAPIs.DSHOW);
        Assert.That(first.Select(s => s.DisplayKey).Distinct().Count(), Is.EqualTo(2));
        IReadOnlyList<CaptureSourceDescriptor> reordered = CaptureSourceCatalog.Describe(
            [("same name", "stable-B", 0), ("same name", "stable-A", 1)], VideoCaptureAPIs.DSHOW);
        Assert.That(CaptureSourceCatalog.Resolve(first[0], reordered).Index, Is.EqualTo(1));
        Assert.Throws<InvalidOperationException>(() => CaptureSourceCatalog.Resolve(first[0], [reordered[0]]));
        IReadOnlyList<CaptureSourceDescriptor> temporary = CaptureSourceCatalog.Describe(
            [("temporary", null, 0)], VideoCaptureAPIs.V4L2);
        IReadOnlyList<CaptureSourceDescriptor> changed = CaptureSourceCatalog.Describe(
            [("different camera", null, 0)], VideoCaptureAPIs.V4L2);
        Assert.That(temporary[0].Persistent, Is.False);
        Assert.Throws<InvalidOperationException>(() => CaptureSourceCatalog.Resolve(temporary[0], changed));
    }

    [Test]
    public void Profiles_PersistAndOverrideOneModule_WithoutReinterpretingBackendIndices()
    {
        CaptureSourceDescriptor source = CaptureSourceCatalog.Describe([("A", "stable-A", 1)], VideoCaptureAPIs.DSHOW)[0];
        HostProfiles original = new()
        {
            Controller = new("COM3"),
            Capture = new(source, 1, "DSHOW", new FrameNormalization(new Rect(1, 2, 80, 45)), source.DisplayKey),
        };
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "profiles-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(original));
            HostProfiles loaded = HostProfiles.Load(path);
            Assert.That(loaded, Is.EqualTo(original));
            HostProfiles inputChanged = loaded.WithOverrides(port: "COM5");
            Assert.That(inputChanged.Capture, Is.SameAs(loaded.Capture));
            Assert.That(inputChanged.Controller!.Endpoint, Is.EqualTo("COM5"));
            HostProfiles sourceChanged = loaded.WithOverrides(captureIndex: 2);
            Assert.That(sourceChanged.Controller, Is.SameAs(loaded.Controller));
            Assert.That(sourceChanged.Capture!.Index, Is.EqualTo(2));
            Assert.That(loaded.WithOverrides(backend: "MSMF").Capture!.Index, Is.Null,
                "不能把 DSHOW 枚举序号直接用到 MSMF");
        }
        finally { File.Delete(path); }
    }
}