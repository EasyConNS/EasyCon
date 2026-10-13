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
using System.Globalization;
using System.Text;

namespace EasyCon.Tests.Capabilities;

[TestFixture]
public class AudioCapabilityTests
{
    [TestCase("audio-lookback.ecs")]
    [TestCase("audio-wait.ecs")]
    [TestCase("audio-wait-score.ecs")]
    public void PublishedExamplesCompile(string file)
    {
        CompileResult result = Compile(ReadExample(file));
        Assert.That(result.Image!.Features & EcsImageFeatures.Audio, Is.Not.Zero);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void PublishedWaitScoreExamplePrintsTheActualNumericScore(bool hit)
    {
        AudioTestRig rig = new();
        RecordingConsole console = new();
        using CapabilityLease lease = Assemble(new AudioService(rig), console: console);
        rig.PlayOnDelay(hit ? rig.Sound : new byte[128000]);
        Run(Compile(ReadExample("audio-wait-score.ecs")), lease);
        string output = console.Output.ToString();
        const string prefix = "本次等待最高分：";
        string scoreLine = output.Split('\n').Single(line => line.StartsWith(prefix, StringComparison.Ordinal));
        Assert.That(double.TryParse(scoreLine[prefix.Length..], CultureInfo.InvariantCulture, out double score), Is.True, output);
        Assert.That(score, Is.EqualTo(hit ? rig.ReferenceScore() : 0).Within(1e-6));
        Assert.That(output, Does.Contain(hit ? "检测到闪光音效，结束脚本" : "等待超时，指定时间内未确认命中"));
        Assert.That(rig.Time, Is.EqualTo(AudioTestRig.Epoch + (hit ? 600 : 8000) * 1_000_000L));
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

    [TestCase(true)]
    [TestCase(false)]
    public void WaitScoreReturnsADoubleAfterHitOrTimeoutAndIsIsolatedPerRun(bool hit)
    {
        AudioTestRig rig = new();
        using CapabilityLease lease = Assemble(new AudioService(rig));
        rig.PlayOnDelay(hit ? rig.Sound : new byte[32000]);
        CompileResult script = Compile("$hit = AUDIO_WAIT(\"tone\", 2000)\nRETURN AUDIO_WAIT_SCORE()");
        Assert.That(Run(script, lease).AsDouble(), Is.EqualTo(hit ? rig.ReferenceScore() : 0).Within(1e-12));
        Assert.That(rig.Source.Subscribers, Is.Zero);
        CompileResult readOnly = Compile("RETURN AUDIO_WAIT_SCORE()");
        Assert.That(readOnly.Image!.Features & EcsImageFeatures.Audio, Is.Not.Zero);
        Assert.That(Assert.Throws<ScriptException>(() => Run(readOnly, lease))!.Message,
            Does.Contain("需要先完成一次 AUDIO_WAIT"));
        Assert.That(rig.Source.Subscribers, Is.Zero);
        Assert.That(rig.Source.StateSubscribers, Is.Zero);
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
        CompileResult score = Compile("A 600\nRETURN AUDIO_WAIT_SCORE()");
        Assert.That(Assert.Throws<ScriptException>(() => Run(score, lease))!.Message, Does.Contain("未提供脚本音频能力"));
        Assert.That(rig.Time, Is.EqualTo(AudioTestRig.Epoch));
        Assert.That(EcxInterpreter.Run(Compile("RETURN AUDIO_WAIT_SCORE()").Image!, new EcxHost()),
            Is.EqualTo(EcxInterpreter.ERR_NOSUCHNATIVE));
    }

    [TestCase("AUDIO_WAIT(\"tone\", 1000)")]
    [TestCase("AUDIO_WAIT_SCORE()")]
    public void McuRejectsAudioAtLoadEvenWithoutStrictCapabilities(string call)
    {
        string directory = Path.Combine(Path.GetTempPath(), "easycon-audio-vm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string? binary = CvmRunner.EnsureBuilt(directory);
            if (binary == null)
                Assert.Ignore("No C compiler available.");
            CompileResult script = Compile($"A 600\nRETURN {call}");
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
            Assert.That(Assert.Throws<InvalidOperationException>(() => session.GetWaitScore(default))!.Message,
                Does.Contain("需要先完成一次 AUDIO_WAIT"));
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

    private static string ReadExample(string file)
    {
        string root = new DirectoryInfo(CvmRunner.FindNativeDir()!).Parent!.Parent!.Parent!.FullName;
        return File.ReadAllText(Path.Combine(root, "examples", file));
    }

    private static CapabilityLease Assemble(IAudioService? audio = null, IPadInput? pad = null, IConsoleIo? console = null)
        => ScriptHostAssembler.Assemble(new ScriptHostContext
        {
            AudioService = audio,
            PadInput = pad,
            Console = console,
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
            public double GetWaitScore(CancellationToken token) => detection.GetWaitScore(token);
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

    private sealed class RecordingConsole : IConsoleIo
    {
        public StringBuilder Output { get; } = new();
        public void Print(string message, bool newline = true)
        {
            Output.Append(message);
            if (newline)
                Output.Append('\n');
        }
        public void Alert(string message) => Assert.Fail("The example should report results through PRINT.");
    }
}