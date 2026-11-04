using EasyCon.Capture;
using EasyCon.Core.Config;
using EasyCon2.Avalonia.Services;
using OpenCvSharp;

namespace EasyCon2.Avalonia.Core.Tests;

public partial class ScriptServiceTests
{
    [Test]
    public async Task ExistingPngRoi_RunsThroughGuiEntry_WithoutConnectingEitherModule()
    {
        FakeLogService log = new();
        FakeDeviceService device = new();
        FakeCaptureService capture = new();
        ScriptService service = CreateService(log, device, capture);
        using Mat image = new(18, 32, MatType.CV_8UC3, Scalar.All(71));
        string png = Convert.ToBase64String(image.ToBytes(".png"));
        string code = $"$img = \"{png}\"\n$part = ROI($img, 0, 0, 8, 8)\nPRINT $part";
        Assert.That(await service.CompileAsync(code, null), Is.True);
        Assert.That(service.GetRequirements().CanRun, Is.True);
        service.RunFromContent(code);
        await WaitUntilAsync(() => !service.IsRunning, "已有图片的 GUI 运行未结束");
        string result = log.Snapshot().First(line => line.StartsWith("iVBOR"));
        using Mat cropped = Cv2.ImDecode(Convert.FromBase64String(result), ImreadModes.Color);
        Assert.Multiple(() =>
        {
            Assert.That(cropped.Size(), Is.EqualTo(new Size(8, 8)));
            Assert.That(device.AutoConnectCalls, Is.Zero);
            Assert.That(device.IsConnected, Is.False);
            Assert.That(capture.IsConnected, Is.False);
            Assert.That(capture.SnapshotSetPropertiesCalls(), Is.Empty);
        });
    }

    [TestCase("OCR_INIT \"eng\"", false)]
    [TestCase("$image = FRAME()\nPRINT $image", true)]
    [TestCase("$text = OCR(0, 0, 8, 8)\nPRINT $text", true)]
    public async Task GuiRequirements_OnlyGateOperationsThatActuallyReadCapture(string code, bool needsCapture)
    {
        ScriptService service = CreateService(new FakeLogService(), new FakeDeviceService(), new FakeCaptureService());
        Assert.That(await service.CompileAsync(code, null), Is.True);
        Assert.That(service.GetRequirements().NeedImageRecognition, Is.EqualTo(needsCapture));
        Assert.That(service.GetRequirements().CanRun, Is.EqualTo(!needsCapture));
    }

    [Test]
    public async Task OcrInit_RunsThroughGuiEntry_WithoutConnectingHardware()
    {
        FakeLogService log = new();
        FakeDeviceService device = new();
        FakeCaptureService capture = new();
        ScriptService service = CreateService(log, device, capture);
        service.RunFromContent("OCR_INIT \"eng\"\nPRINT 1");
        await WaitUntilAsync(() => !service.IsRunning, "OCR_INIT 的 GUI 运行未结束");
        Assert.That(log.Snapshot(), Does.Contain("脚本运行完成"));
        Assert.That(device.AutoConnectCalls, Is.Zero);
        Assert.That(capture.IsConnected, Is.False);
        Assert.That(capture.SnapshotSetPropertiesCalls(), Is.Empty);
    }

    [Test]
    public void GuiSettings_UseTheSameSourceConsistencyValidationAsCliProfiles()
    {
        CaptureSourceDescriptor source = new("DSHOW:stable-A", "source-A", 1, VideoCaptureAPIs.DSHOW, true, "saved-revision");
        ConfigState valid = new() { ControllerProfile = new("COM3"), CaptureProfile = new(source, 1, "DSHOW") };
        FakeLogService log = new();
        UserSettingsService settings = new(load: () => valid, log: log);
        settings.Load(state => Assert.That(state.CaptureProfile!.Source, Is.EqualTo(source)));
        ConfigState invalid = valid with { CaptureProfile = valid.CaptureProfile! with { Index = 7 } };
        UserSettingsService badSettings = new(load: () => invalid, log: log);
        badSettings.Load(state => Assert.That(state.CaptureProfile, Is.Null));
        Assert.That(log.Snapshot().Any(line => line.Contains("缓存索引") && line.Contains("矛盾")), Is.True);
    }
}