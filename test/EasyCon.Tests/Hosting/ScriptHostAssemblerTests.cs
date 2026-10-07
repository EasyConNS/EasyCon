using EasyCon.Core.Capabilities;
using EasyCon.Core.Hosting;
using EasyCon.Script;
using EasyScript;

namespace EasyCon.Tests.Hosting;

/// <summary>
/// 组合根锁定（docs/Framework.md §5.1）：能力的默认值、适配与所有权只有一处实现。
///
/// 这些断言防的是**已经发生过的漂移**：收敛前 GUI 与 CLI 各手写一份 CapabilitySet 字面量，
/// 结果 CLI 只 Dispose <c>Ocr</c> 不 Dispose <c>Inference</c>、两个宿主都不装配
/// <see cref="IHostEnvironment"/>（ARG/APP 走 EcxVm 硬编码回落）。
/// </summary>
[TestFixture]
public class ScriptHostAssemblerTests
{
    [Test]
    public void Assemble_FillsDefaults_AndLeavesUnsuppliedCapabilitiesNull()
    {
        using CapabilityLease lease = ScriptHostAssembler.Assemble(new ScriptHostContext());

        CapabilitySet caps = lease.Capabilities;
        Assert.Multiple(() =>
        {
            // 恒装配的默认项
            Assert.That(caps.Environment, Is.Not.Null, "IHostEnvironment 必须恒装配（ARG/APP 不再靠 EcxVm 回落）");
            Assert.That(caps.Files, Is.SameAs(DesktopFileSystem.Instance));
            Assert.That(caps.Ocr, Is.Not.Null);
            Assert.That(caps.Ocr!.Backend, Is.EqualTo("tesseract"));
            Assert.That(caps.Inference, Is.Not.Null);

            // "null = 不可用"：宿主没给的原料不得静默造替身
            Assert.That(caps.Input, Is.Null);
            Assert.That(caps.Console, Is.Null);
            Assert.That(caps.Capture, Is.Null);
            Assert.That(caps.Vision, Is.Null);
        });
    }

    [Test]
    public void Assemble_MapsRawMaterialToCapabilities()
    {
        var pad = new FakePad();
        var io = new FakeConsole();

        using CapabilityLease lease = ScriptHostAssembler.Assemble(new ScriptHostContext
        {
            Pad = pad,
            Console = io,
            Frame = (x, y, w, h) => $"frame:{x},{y},{w},{h}",
            Roi = (b64, x, y, w, h) => $"roi:{b64}:{w}x{h}",
            LabelMatch = name => name == "hit" ? 7 : -1,
            Args = ["a1", "a2"],
            AppDir = "/tmp/easycon-app",
            EnableOcr = false,
            EnableInference = false,
        });

        CapabilitySet caps = lease.Capabilities;
        ICaptureSource capture = caps.Capture!;
        IVisionService vision = caps.Vision!;
        IHostEnvironment environment = caps.Environment!;
        IPadInput input = caps.Input!;

        Assert.Multiple(() =>
        {
            Assert.That(capture.CaptureFrame(1, 2, 3, 4), Is.EqualTo("frame:1,2,3,4"));
            Assert.That(vision.Crop("B64", 0, 0, 5, 6), Is.EqualTo("roi:B64:5x6"));
            Assert.That(vision.MatchLabel("hit"), Is.EqualTo(7));
            Assert.That(vision.MatchLabel("miss"), Is.EqualTo(-1));
            Assert.That(environment.Args, Is.EqualTo(new[] { "a1", "a2" }));
            Assert.That(environment.AppDir, Is.EqualTo("/tmp/easycon-app"));

            // 显式关闭时不得装配默认后端
            Assert.That(caps.Ocr, Is.Null);
            Assert.That(caps.Inference, Is.Null);

            // Console 直传（不做二次包装）
            Assert.That(caps.Console, Is.SameAs(io));
        });

        // ICGamePad → IPadInput 的适配由装配器承担（宿主不再各自 new PadInputAdapter）
        input.ClickButtons(GamePadKey.A, 30, CancellationToken.None);
        input.SetStick(GamePadKey.LS, 10, 20);
        Assert.That(pad.Events, Is.EqualTo(new[] { "click A 30", "stick LS 10 20" }));
    }

    [Test]
    public void Assemble_VisionRequiresRoiOrLabelMatch()
    {
        using CapabilityLease frameOnly = ScriptHostAssembler.Assemble(new ScriptHostContext
        {
            Frame = (x, y, w, h) => "f",
        });
        Assert.Multiple(() =>
        {
            Assert.That(frameOnly.Capabilities.Capture, Is.Not.Null, "有帧委托即装配 Capture");
            Assert.That(frameOnly.Capabilities.Vision, Is.Null, "只有帧、无 ROI/标签时不装配 Vision");
        });

        using CapabilityLease labelOnly = ScriptHostAssembler.Assemble(new ScriptHostContext
        {
            LabelMatch = _ => 1,
        });
        Assert.Multiple(() =>
        {
            Assert.That(labelOnly.Capabilities.Capture, Is.Null);
            Assert.That(labelOnly.Capabilities.Vision, Is.Not.Null);
            // 未提供 Roi 时 Crop 返回 null（IVisionService 契约），不抛错
            Assert.That(labelOnly.Capabilities.Vision!.Crop("B64", 0, 0, 1, 1), Is.Null);
        });
    }

    [Test]
    public void Lease_TakesOwnershipOfOcrAndInference()
    {
        var ocr = new FakeOcr();
        var inference = new FakeInference();

        CapabilityLease lease = ScriptHostAssembler.Assemble(new ScriptHostContext
        {
            Ocr = ocr,
            Inference = inference,
        });

        Assert.Multiple(() =>
        {
            Assert.That(lease.Capabilities.Ocr, Is.SameAs(ocr));
            Assert.That(lease.Capabilities.Inference, Is.SameAs(inference));
            Assert.That(ocr.Disposed, Is.False);
        });

        lease.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(ocr.Disposed, Is.True, "租约接管传入的 Ocr 所有权");
            Assert.That(inference.Disposed, Is.True, "租约接管传入的 Inference 所有权");
            Assert.DoesNotThrow(() => lease.Dispose(), "重复 Dispose 必须安全");
        });
    }

    sealed class FakePad : ICGamePad
    {
        public List<string> Events { get; } = [];

        public DelayType DelayMethod => DelayType.Normal;

        public void ClickButtons(GamePadKey key, int duration, CancellationToken token)
            => Events.Add($"click {key} {duration}");

        public void PressButtons(GamePadKey key) => Events.Add($"press {key}");

        public void ReleaseButtons(GamePadKey key) => Events.Add($"release {key}");

        public void ClickStick(GamePadKey key, byte x, byte y, int duration, CancellationToken token)
            => Events.Add($"clickstick {key} {x} {y} {duration}");

        public void SetStick(GamePadKey key, byte x, byte y) => Events.Add($"stick {key} {x} {y}");

        public void ChangeAmiibo(uint index) => Events.Add($"amiibo {index}");

        public void Reset() => Events.Add("reset");
    }

    sealed class FakeConsole : IConsoleIo
    {
        public List<string> Lines { get; } = [];

        public void Print(string message, bool newline = true) => Lines.Add(message);

        public void Alert(string message) => Lines.Add($"ALERT:{message}");
    }

    sealed class FakeOcr : IOcrService
    {
        public bool Disposed { get; private set; }

        public string Backend => "fake";

        public int LastConfidence => 0;

        public bool Init(OcrConfig cfg) => true;

        public string Recognize(ImageRef image, OcrQuery query) => "";

        public void Dispose() => Disposed = true;
    }

    sealed class FakeInference : IInference
    {
        public bool Disposed { get; private set; }

        public int Load(string modelPath) => -1;

        public float[]? Run(int session, float[] input) => null;

        public void Unload(int session)
        {
        }

        public int[]? LastOutputShape => null;
        public int HoldTensor(float[] data, int[] shape) => -1;

        public void FreeTensor(int handle) { }

        public float[]? RunHeld(int session, int tensorHandle) => null;

        public int TransformTensor(int handle, float scale, float offset) => -1;

        public void Dispose() => Disposed = true;
    }
}

/// <summary>
/// 编译档位锁定（档位塌缩后，SingleStreamFormat §2 R-5）：产物同形态，
/// 剩余两档只是缓存策略（与产物内容无关，不进缓存键）。
/// </summary>
[TestFixture]
public class ScriptCompileProfilesTests
{
    [Test]
    public void Interactive_SkipsDiskCache()
    {
        CompileOptions options = ScriptCompileProfiles.Interactive(["lbl"], optimize: false);

        Assert.Multiple(() =>
        {
            Assert.That(options.UseDiskCache, Is.False, "现编档不落盘（run/编辑器高频重编）");
            Assert.That(options.UseProcessCache, Is.True, "进程缓存兜底");
            Assert.That(options.Optimize, Is.False);
            Assert.That(options.ExtVars, Is.EquivalentTo(new[] { "lbl" }));
        });
    }

    [Test]
    public void Distributable_EnablesDiskCache()
    {
        CompileOptions options = ScriptCompileProfiles.Distributable();

        Assert.Multiple(() =>
        {
            Assert.That(options.UseDiskCache, Is.True, "obj/ 内容寻址缓存服务分发产物");
            Assert.That(options.Optimize, Is.True);
            Assert.That(options.ExtVars, Is.Null);
        });
    }

    [Test]
    public void Profiles_ShareProductFingerprint()
    {
        // 档位塌缩：两档产物同形态（差异仅在缓存策略，而缓存策略不进键）→ 共享缓存条目
        Assert.That(
            ScriptCompileProfiles.Interactive([]).ProductFingerprint(),
            Is.EqualTo(ScriptCompileProfiles.Distributable([]).ProductFingerprint()));
    }
}