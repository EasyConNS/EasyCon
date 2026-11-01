using EasyCon.Capture;
using EasyCon.Core.Config;
using EasyCon.Core.Hosting;
using EasyCon.Core.Script;
using OpenCvSharp;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyCon.Tests.Maa;

[TestFixture]
public sealed class HostProfileRegressionTests
{
    private static HostProfiles Saved()
    {
        CaptureSourceDescriptor source = CaptureSourceCatalog.Describe([("same", "A", 1)], VideoCaptureAPIs.DSHOW)[0];
        return new HostProfiles { Controller = new("COM3"), Capture = new(source, 1, "DSHOW", DisplayKey: source.DisplayKey) };
    }

    [Test]
    public void BridgeOverride_UpdatesTransport_AndPreservesCapture()
    {
        HostProfiles original = Saved();
        HostProfiles bridge = original.WithOverrides(port: "tcp://localhost:31789");
        Assert.That(bridge.Controller!.Transport, Is.EqualTo("TcpBridge"));
        Assert.That(bridge.Capture, Is.SameAs(original.Capture));
        HostProfiles serial = bridge.WithOverrides(port: "COM5");
        Assert.That(serial.Controller!.Transport, Is.EqualTo("Serial"));
        Assert.That(serial.Capture, Is.SameAs(original.Capture));
    }

    [Test]
    public void BridgeProfile_RoundTripsGuiConfigurationAndCliLoader()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid() + ".json");
        ConfigState settings = new() { ControllerProfile = ControllerProfile.FromEndpoint("tcp://localhost:31789") };
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(settings));
            HostProfiles profiles = HostProfiles.Load(path);
            Assert.That(profiles.Controller, Is.EqualTo(settings.ControllerProfile));
        }
        finally { File.Delete(path); }
    }

    [TestCase("tcp://localhost:31789", "Serial")]
    [TestCase("COM3", "TcpBridge")]
    [TestCase("tcp://localhost", "TcpBridge")]
    [TestCase("COM3", "unknown")]
    public void ControllerProfile_RejectsTransportMismatch(string endpoint, string transport)
        => Assert.Throws<InvalidDataException>(() => new ControllerProfile(endpoint, transport).Validate());

    [TestCase("DSHOW")]
    [TestCase("dshow")]
    public void SameBackendOverride_PreservesIdentity_AndResolvesReordering(string backend)
    {
        HostProfiles original = Saved();
        HostProfiles overridden = original.WithOverrides(backend: backend);
        IReadOnlyList<CaptureSourceDescriptor> current = CaptureSourceCatalog.Describe(
            [("same", "A", 0), ("same", "B", 1)], VideoCaptureAPIs.DSHOW);
        Assert.Multiple(() =>
        {
            Assert.That(overridden.Capture!.Source, Is.SameAs(original.Capture!.Source));
            Assert.That(CaptureSourceCatalog.Resolve(overridden.Capture.Source!, current).Index, Is.Zero);
            Assert.That(overridden.Controller, Is.SameAs(original.Controller));
        });
    }

    [Test]
    public void BackendAliasesCompareByEnum_AndExplicitIndexIntentionallyReplacesIdentity()
    {
        CaptureSourceDescriptor source = CaptureSourceCatalog.Describe([("Linux", "A", 1)], VideoCaptureAPIs.V4L2)[0];
        HostProfiles original = new() { Capture = new(source, 1, "V4L2") };
        Assert.That(original.WithOverrides(backend: "V4L").Capture!.Source, Is.SameAs(source));
        HostProfiles selected = Saved().WithOverrides(captureIndex: 7);
        Assert.That(selected.Capture!.Source, Is.Null);
        Assert.That(selected.Capture.Index, Is.EqualTo(7));
        Assert.That(selected.Capture.DisplayKey, Is.Null);
        HostProfiles changed = Saved().WithOverrides(backend: "MSMF");
        Assert.That(changed.Capture!.Source, Is.Null);
        Assert.That(changed.Capture.Index, Is.Null);
        Assert.That(Saved().WithOverrides(port: "COM5").Capture, Is.EqualTo(Saved().Capture));
    }

    [TestCase("index")]
    [TestCase("backend")]
    [TestCase("unknown")]
    [TestCase("numeric")]
    [TestCase("negative")]
    public void Load_RejectsContradictoryAndInvalidProfiles_BeforeAnyDeviceIsOpened(string problem)
    {
        HostProfiles original = Saved();
        CaptureProfile capture = problem switch
        {
            "index" => original.Capture! with { Index = 7 },
            "backend" => original.Capture! with { Backend = "MSMF" },
            "unknown" => original.Capture! with { Backend = "not-a-backend" },
            "numeric" => original.Capture! with { Backend = "700" },
            _ => original.Capture! with { Index = -1 },
        };
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(original with { Capture = capture },
                new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } }));
            Assert.Throws<InvalidDataException>(() => HostProfiles.Load(path));
        }
        finally { File.Delete(path); }
    }

    [TestCase("PRINT 42", false)]
    [TestCase("$img = FRAME()\nPRINT $img", true)]
    [TestCase("$part = ROI(\"image\", 0, 0, 8, 8)\nPRINT $part", false)]
    [TestCase("OCR_INIT \"eng\"", false)]
    [TestCase("$txt = OCR(0, 0, 8, 8, \"eng\")\nPRINT $txt", true)]
    public void HardwareRequirements_DistinguishExplicitImagesFromCapture(string code, bool expected)
    {
        IScriptSession session = new EasyScriptEngine().FromSource(code,
            new ScriptHostOptions { Compile = ScriptCompileProfiles.Interactive([]) });
        Assert.That(session.Info.Diagnostics.Where(d => d.IsError), Is.Empty);
        Assert.That(ScriptHardwareRequirements.RequiresLiveCapture(session.Info), Is.EqualTo(expected));
    }
}