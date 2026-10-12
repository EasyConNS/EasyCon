using EasyCon.Capture.Audio;
using EasyCon.Core.Capabilities;
using EasyCon.Core.Hosting;
using EasyCon.Core.Runner;
using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Script.Runtime;
using EasyCon.Script.Symbols;
using EasyCon.Tests.Support;
using EasyScript;

namespace EasyCon.Tests.Capabilities;

[TestFixture]
public class AudioCapabilityTests
{
    [TestCase("audio-lookback.ecs")]
    [TestCase("audio-wait.ecs")]
    public void PublishedExamplesCompile(string file)
    {
        string root = new DirectoryInfo(CvmRunner.FindNativeDir()!).Parent!.Parent!.Parent!.FullName;
        CompileResult result = Compile(File.ReadAllText(Path.Combine(root, "examples", file)));
        Assert.That(result.Image!.Features & EcsImageFeatures.Audio, Is.Not.Zero);
    }

    [Test]
    public void AudioDuringKeysIsAvailableBeforeTheFirstQueryAndEachRunIsIsolated()
    {
        AudioTestRig rig = new();
        AudioService audio = new(rig);
        using CapabilityLease lease = Assemble(audio, new AudioPad(rig));
        CompileResult script = Compile("A 600\nRETURN AUDIO(\"tone\", 5000)");
        Assert.That(script.Image!.Features & EcsImageFeatures.Audio, Is.Not.Zero);
        Assert.That(Run(script, lease).AsDouble(), Is.EqualTo(rig.ReferenceScore()).Within(1e-12));
        Assert.That(rig.Source.Subscribers, Is.Zero);
        ScriptException? error = Assert.Throws<ScriptException>(() => Run(Compile("RETURN AUDIO(\"tone\", 1000)"), lease));
        Assert.That(error!.Message, Does.Contain("历史不足"));
        Assert.That(audio.Runs, Is.EqualTo(2));
        Assert.That(rig.Source.StateSubscribers, Is.Zero);
    }

    [Test]
    public void BuiltinDefaultThresholdAndReturnTypesReachTheAudioPort()
    {
        AudioTestRig rig = new();
        AudioService audio = new(rig);
        using CapabilityLease lease = Assemble(audio);
        rig.PlayOnDelay(rig.Sound);
        Assert.That(Run(Compile("RETURN AUDIO_WAIT(\"tone\", 2000)"), lease).AsBool(), Is.True);
        Assert.That(audio.Thresholds, Is.EqualTo(new[] { 0.85 }));
        rig.PlayOnDelay(new byte[16000]);
        Assert.That(Run(Compile("RETURN AUDIO_WAIT(\"tone\", 1000, 1)"), lease).AsBool(), Is.False);
        Assert.That(audio.Thresholds, Is.EqualTo(new[] { 0.85, 1.0 }));
        Assert.That(Assert.Throws<ScriptException>(() => Run(Compile("RETURN AUDIO_WAIT(\"tone\", 1000, 85)"), lease))!.Message,
            Does.Contain("threshold"));
        Assert.That(rig.Source.Subscribers, Is.Zero);
    }

    [Test]
    public void CancellationIsNotAFalseResultAndClosesAllListeners()
    {
        AudioTestRig rig = new();
        using CancellationTokenSource cancellation = new();
        using CapabilityLease lease = Assemble(new AudioService(rig));
        rig.AfterDelay = () => cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => Run(Compile("RETURN AUDIO_WAIT(\"tone\", 8000)"), lease, cancellation.Token));
        Assert.That(rig.Source.Subscribers, Is.Zero);
        Assert.That(rig.Source.StateSubscribers, Is.Zero);
        Assert.That(rig.Source.Disposed, Is.False);
    }

    [Test]
    public void MissingCapabilityFailsBeforeAnyKeyAndBareInterpreterDoesNotDegrade()
    {
        AudioTestRig rig = new();
        using CapabilityLease lease = Assemble(pad: new AudioPad(rig));
        CompileResult script = Compile("A 600\nRETURN AUDIO(\"tone\", 1000)");
        Assert.That(Assert.Throws<ScriptException>(() => Run(script, lease))!.Message, Does.Contain("未提供脚本音频能力"));
        Assert.That(rig.Time, Is.EqualTo(AudioTestRig.Epoch));
        CompileResult wait = Compile("RETURN AUDIO_WAIT(\"tone\", 1000)");
        Assert.That(EcxInterpreter.Run(wait.Image!, new EcxHost()), Is.EqualTo(EcxInterpreter.ERR_NOSUCHNATIVE));
    }

    [Test]
    public void McuRejectsAudioAtLoadEvenWithoutStrictCapabilities()
    {
        string directory = Path.Combine(Path.GetTempPath(), "easycon-audio-vm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string? binary = CvmRunner.EnsureBuilt(directory);
            if (binary == null)
                Assert.Ignore("No C compiler available.");
            CompileResult script = Compile("A 600\nRETURN AUDIO_WAIT(\"tone\", 1000)");
            (int exit, string output, string error) = CvmRunner.Run(binary!, EcsContainer.WriteImage(script.Image!), "audio", directory);
            Assert.That(exit, Is.EqualTo(14), error);
            Assert.That(output, Is.Empty);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void LabelsResolveOnlyUnderTheSavedScriptAndFailuresAreExplicit()
    {
        string directory = Path.Combine(Path.GetTempPath(), "easycon-script-audio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            AudioTestRig rig = new();
            Assert.Throws<InvalidOperationException>(() => new AudioLabelService(rig.Source, null).BeginRun(default));
            using IAudioSession session = new AudioLabelService(rig.Source, directory).BeginRun(default);
            Assert.That(Assert.Throws<InvalidOperationException>(() => session.Match("missing", 1000, default))!.Message,
                Does.Contain(Path.Combine(directory, "audiolabel", "missing.audiolabel", "label.json")));
            Assert.Throws<ArgumentException>(() => session.Match("../other", 1000, default));
            string path = AudioLabelStore.Save(Path.Combine(directory, "audiolabel"), "tone",
                new AudioClip(rig.Source.State.Format!, rig.Sound));
            Assert.That(Assert.Throws<InvalidOperationException>(() => session.Match("tone", 1000, default))!.Message,
                Does.Contain("历史不足"), "Valid label loads successfully, but an empty buffer is not a valid score.");
            File.WriteAllText(path, "{invalid");
            using IAudioSession nextRun = new AudioLabelService(rig.Source, directory).BeginRun(default);
            Assert.That(Assert.Throws<InvalidOperationException>(() => nextRun.Match("tone", 1000, default))!.Message, Does.Contain("无法加载"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static CapabilityLease Assemble(IAudioService? audio = null, IPadInput? pad = null)
        => ScriptHostAssembler.Assemble(new ScriptHostContext
        {
            AudioService = audio,
            PadInput = pad,
            EnableOcr = false,
            EnableInference = false,
        });

    private static CompileResult Compile(string source)
    {
        CompileResult result = Compilation.CompileSource(source, ScriptCompileProfiles.Interactive());
        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty,
            string.Join("; ", result.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
        return result;
    }

    private static Value Run(CompileResult script, CapabilityLease lease, CancellationToken token = default)
        => EcxVm.Run(script.Image!, lease.Capabilities, token, nativeSymbols: script.NativeSymbols);

    private sealed class AudioService(AudioTestRig rig) : IAudioService
    {
        public int Runs { get; private set; }
        public List<double> Thresholds { get; } = [];
        public IAudioSession BeginRun(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Runs++;
            return new Session(rig.Open(), Thresholds);
        }

        private sealed class Session(AudioDetectionSession detection, List<double> thresholds) : IAudioSession
        {
            public double Match(string name, int milliseconds, CancellationToken token) => detection.Match(name, milliseconds, token);
            public bool Wait(string name, int milliseconds, double threshold, CancellationToken token)
            {
                thresholds.Add(threshold);
                return detection.Wait(name, milliseconds, threshold, token);
            }
            public void Dispose() => detection.Dispose();
        }
    }

    private sealed class AudioPad(AudioTestRig rig) : IPadInput
    {
        public void ClickButtons(GamePadKey key, int duration, CancellationToken token)
        {
            Assert.That(rig.Source.Subscribers, Is.EqualTo(1), "The listener must precede the very first key operation.");
            rig.Feed(rig.Sound);
        }
        public void PressButtons(GamePadKey key) { }
        public void ReleaseButtons(GamePadKey key) { }
        public void ClickStick(GamePadKey key, byte x, byte y, int duration, CancellationToken token) { }
        public void SetStick(GamePadKey key, byte x, byte y) { }
        public void ChangeAmiibo(uint index) { }
    }
}