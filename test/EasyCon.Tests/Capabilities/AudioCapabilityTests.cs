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
using System.Collections.Concurrent;

namespace EasyCon.Tests.Capabilities;

[TestFixture]
public class AudioCapabilityTests
{
    private string _directory = null!;
    private TestAudioSource _source = null!;
    private byte[] _sound = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "easycon-script-audio-" + Guid.NewGuid().ToString("N"));
        _source = new TestAudioSource(new AudioFormat(8000, 1, 16, "pcm"), AudioClock.NowNanoseconds);
        _sound = AudioTestRig.MakeSound();
        AudioLabelStore.Save(Path.Combine(_directory, "audiolabel"), "医疗", new AudioClip(_source.State.Format!, _sound));
        AudioLabelStore.Save(Path.Combine(_directory, "audiolabel"), "other",
            new AudioClip(_source.State.Format!, AudioTestRig.MakeSound(frequencyShift: 1400)));
    }

    [TearDown]
    public void TearDown()
    {
        _source.Dispose();
        Directory.Delete(_directory, true);
    }

    [TestCase("audio-load-score.ecs")]
    [TestCase("audio-reload.ecs")]
    [TestCase("audio-observe.ecs")]
    public void PublishedExamplesCompileAndRun(string file)
    {
        ExampleAudio audio = new();
        TestPad pad = new((_, _, _) => audio.Peak = 92);
        using CapabilityLease lease = Assemble(audio, pad);
        CompileResult result = Compile(ReadExample(file));
        Assert.That(result.Image!.Features & EcsImageFeatures.Audio, Is.Not.Zero);
        Run(result, lease);
        Assert.That(audio.Disposed, Is.True);
        Assert.That(audio.Loaded, Is.False);
        if (file == "audio-load-score.ecs")
        {
            Assert.That(pad.Keys, Is.EqualTo(new[] { GamePadKey.A, GamePadKey.B, GamePadKey.B, GamePadKey.B, GamePadKey.B, GamePadKey.B }));
            Assert.That(audio.Loads, Is.EqualTo(1));
        }
        else if (file == "audio-reload.ecs")
            Assert.That(audio.Loads, Is.EqualTo(2));
    }

    [TestCase("RETURN AUDIO(\"医疗\", 1000)")]
    [TestCase("RETURN AUDIO_WAIT(\"医疗\", 1000)")]
    [TestCase("RETURN AUDIO_WAIT_SCORE()")]
    public void RemovedFunctionsNoLongerCompile(string script)
    {
        CompileResult result = Compilation.CompileSource(script, ScriptCompileProfiles.Interactive());
        Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.IsError), Is.True);
    }

    [TestCase("WAIT 1000")]
    [TestCase("A 1000")]
    public async Task MatchingRunsDuringWaitOrKeysAndReadsDoNotConsumeThePeak(string operation)
    {
        using CancellationTokenSource stop = new(TimeSpan.FromSeconds(10));
        TestPad pad = new((_, duration, token) => CustomDelay.Delay(duration, token));
        using CapabilityLease lease = Assemble(new AudioLabelService(_source, _directory), pad);
        Task playback = PlayAsync(stop.Token);
        try
        {
            CompileResult script = Compile($"AUDIO_LOAD \"医疗\"\n{operation}\n$first = AUDIO_SCORE(\"医疗\")\n$second = AUDIO_SCORE(\"医疗\")\nAUDIO_UNLOAD \"医疗\"\nRETURN $first + $second");
            Value value = await Task.Run(() => Run(script, lease, stop.Token));
            Assert.That(value.Type, Is.EqualTo(ScriptType.Int));
            Assert.That(value.AsInt(), Is.InRange(196, 200));
            Assert.That(_source.Subscribers, Is.Zero);
            Assert.That(_source.StateSubscribers, Is.Zero);
            Assert.That(_source.Disposed, Is.False);
        }
        finally
        {
            await stop.CancelAsync();
            await playback;
        }
    }

    [TestCase(0, 0)]
    [TestCase(0.849, 84)]
    [TestCase(0.85, 85)]
    [TestCase(1, 100)]
    public void AudioScoreReturnsIntAndUsesTheIntegerThreshold(double normalized, int expected)
    {
        ExampleAudio audio = new();
        TestPad pad = new((_, _, _) => audio.Peak = AudioMatchMonitor.ToPercentage(normalized));
        using CapabilityLease lease = Assemble(audio, pad);
        CompileResult script = Compile("""
            AUDIO_LOAD "医疗"
            A
            $score = AUDIO_SCORE("医疗")
            IF $score >= 85
                RETURN $score + 1000
            ENDIF
            RETURN $score
            """);
        Value result = Run(script, lease);
        Assert.That(result.Type, Is.EqualTo(ScriptType.Int));
        Assert.That(result.AsInt(), Is.EqualTo(expected >= 85 ? expected + 1000 : expected));
    }

    [Test]
    public async Task NamesAreIndependentAndReloadAndNewRunsStartAtZero()
    {
        AudioLabelService service = new(_source, _directory);
        using CancellationTokenSource stop = new(TimeSpan.FromSeconds(10));
        using IAudioSession session = service.BeginRun(stop.Token);
        session.Load("医疗", stop.Token);
        session.Load("other", stop.Token);
        Assert.That(session.Score("医疗", default), Is.Zero);
        Task playback = PlayAsync(stop.Token);
        try
        {
            await AudioTestRig.UntilAsync(() => session.Score("医疗", default) > 98);
            Assert.That(session.Score("other", default), Is.LessThan(70));
            Assert.That(Assert.Throws<InvalidOperationException>(() => session.Load("医疗", default))!.Message, Does.Contain("先 AUDIO_UNLOAD"));
            Assert.That(session.Score("医疗", default), Is.GreaterThan(98));
            session.Unload("医疗", default);
            Assert.That(_source.Subscribers, Is.EqualTo(1));
            Assert.Throws<InvalidOperationException>(() => session.Score("医疗", default));
            session.Load("医疗", default);
            Assert.That(session.Score("医疗", default), Is.Zero);
            using IAudioSession next = service.BeginRun(default);
            next.Load("医疗", default);
            Assert.That(next.Score("医疗", default), Is.Zero);
        }
        finally
        {
            await stop.CancelAsync();
            await playback;
        }
    }

    [TestCase("stop")]
    [TestCase("error")]
    [TestCase("disconnect")]
    [TestCase("invalid-audio")]
    public async Task EveryExitCleansBackgroundWorkAndInputFailuresRemainVisible(string exit)
    {
        using CancellationTokenSource stop = new(TimeSpan.FromSeconds(10));
        RecordingConsole console = new();
        using CapabilityLease lease = Assemble(new AudioLabelService(_source, _directory), console: console);
        string tail = exit == "error" ? "RETURN 1 / INT(ARG(0))" : "RETURN 0";
        CompileResult script = Compile("AUDIO_LOAD \"医疗\"\nPRINT \"loaded\"\nWAIT 300\n" + tail);
        Task run = Task.Run(() => Run(script, lease, stop.Token));
        try
        {
            await AudioTestRig.UntilAsync(() => console.Lines.Contains("loaded") || run.IsCompleted);
            Assert.That(console.Lines, Does.Contain("loaded"));
            if (exit == "stop")
                await stop.CancelAsync();
            else if (exit == "disconnect")
                _source.SetState(new(AudioCaptureStatus.Disconnected));
            else if (exit == "invalid-audio")
                _source.Emit([1]);
            if (exit == "stop")
                Assert.ThrowsAsync<OperationCanceledException>(async () => await run);
            else
            {
                ScriptException? error = Assert.ThrowsAsync<ScriptException>(async () => await run);
                if (exit is "disconnect" or "invalid-audio")
                    Assert.That(error!.Message, Does.Contain("后台识别失败"));
            }
            Assert.That(_source.Subscribers, Is.Zero);
            Assert.That(_source.StateSubscribers, Is.Zero);
            Assert.That(_source.Disposed, Is.False);
        }
        finally
        {
            await stop.CancelAsync();
            try { await run; }
            catch (Exception) when (run.IsFaulted || run.IsCanceled) { }
        }
    }

    [TestCase("AUDIO_LOAD \"医疗\"")]
    [TestCase("RETURN AUDIO_SCORE(\"医疗\")")]
    [TestCase("AUDIO_UNLOAD \"医疗\"")]
    public void MissingCapabilityFailsBeforeKeysAndBareInterpreterDoesNotDegrade(string call)
    {
        TestPad pad = new((_, _, _) => Assert.Fail("Missing audio capability must fail before the first key."));
        using CapabilityLease lease = Assemble(pad: pad);
        Assert.That(Assert.Throws<ScriptException>(() => Run(Compile("A\n" + call), lease))!.Message, Does.Contain("未提供脚本音频能力"));
        Assert.That(EcxInterpreter.Run(Compile(call).Image!, new EcxHost()), Is.EqualTo(EcxInterpreter.ERR_NOSUCHNATIVE));
    }

    [TestCase("AUDIO_LOAD \"医疗\"")]
    [TestCase("RETURN AUDIO_SCORE(\"医疗\")")]
    [TestCase("AUDIO_UNLOAD \"医疗\"")]
    public void McuRejectsAudioAtLoadEvenWithoutStrictCapabilities(string call)
    {
        string? binary = CvmRunner.EnsureBuilt(_directory);
        if (binary == null)
            Assert.Ignore("No C compiler available.");
        CompileResult script = Compile("A\n" + call);
        (int exit, string output, string error) = CvmRunner.Run(binary!, EcsContainer.WriteImage(script.Image!), "audio", _directory);
        Assert.That(exit, Is.EqualTo(14), error);
        Assert.That(output, Is.Empty);
    }

    [Test]
    public void LabelsResolveUnderTheSavedScriptAndFailuresAreNotZeroScores()
    {
        Assert.Throws<InvalidOperationException>(() => new AudioLabelService(_source, null).BeginRun(default));
        using IAudioSession session = new AudioLabelService(_source, _directory).BeginRun(default);
        Assert.That(_source.Subscribers, Is.Zero, "Opening a run must not listen before LOAD.");
        Assert.Throws<InvalidOperationException>(() => session.Score("医疗", default));
        Assert.Throws<InvalidOperationException>(() => session.Unload("医疗", default));
        Assert.That(Assert.Throws<InvalidOperationException>(() => session.Load("missing", default))!.Message,
            Does.Contain(Path.Combine(_directory, "audiolabel", "missing.audiolabel", "label.json")));
        Assert.Throws<ArgumentException>(() => session.Load("../other", default));
        session.Load("医疗", default);
        Assert.That(session.Score("医疗", default), Is.Zero);
        session.Unload("医疗", default);
        File.WriteAllText(Path.Combine(_directory, "audiolabel", "医疗.audiolabel", "label.json"), "{invalid");
        Assert.That(Assert.Throws<InvalidOperationException>(() => session.Load("医疗", default))!.Message, Does.Contain("无法加载"));
        Assert.That(_source.Subscribers, Is.Zero);
    }

    [Test]
    public void NonAudioScriptsDoNotRequireASavedPathOrConnectedAudio()
    {
        _source.SetState(new(AudioCaptureStatus.Disconnected));
        using CapabilityLease lease = ScriptHostAssembler.Assemble(new ScriptHostContext
        {
            AudioSource = _source,
            EnableOcr = false,
            EnableInference = false,
        });
        Assert.That(Run(Compile("WAIT 1\nRETURN 42"), lease).AsInt(), Is.EqualTo(42));
        Assert.That(_source.Subscribers, Is.Zero);
        Assert.That(Assert.Throws<ScriptException>(() => Run(Compile("AUDIO_LOAD \"医疗\""), lease))!.Message, Does.Contain("先保存脚本"));
    }

    private async Task PlayAsync(CancellationToken token)
    {
        try
        {
            while (_source.Subscribers == 0)
                await Task.Delay(5, token);
            for (int offset = 0; ; offset += 1600)
            {
                await Task.Delay(100, token);
                _source.Emit(offset < _sound.Length ? _sound.AsSpan(offset, 1600).ToArray() : new byte[1600]);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
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

    private sealed class ExampleAudio : IAudioService, IAudioSession
    {
        public bool Loaded { get; private set; }
        public bool Disposed { get; private set; }
        public int Loads { get; private set; }
        public int Peak { get; set; }
        public IAudioSession BeginRun(CancellationToken token) => this;
        public void Load(string labelName, CancellationToken token) { Assert.That(Loaded, Is.False); Loaded = true; Loads++; Peak = 0; }
        public int Score(string labelName, CancellationToken token) { Assert.That(Loaded, Is.True); return Peak; }
        public void Unload(string labelName, CancellationToken token) { Assert.That(Loaded, Is.True); Loaded = false; }
        public void CheckHealth(CancellationToken token) { }
        public void Dispose() { Loaded = false; Disposed = true; }
    }

    private sealed class TestPad(Action<GamePadKey, int, CancellationToken> click) : IPadInput
    {
        public List<GamePadKey> Keys { get; } = [];
        public void ClickButtons(GamePadKey key, int duration, CancellationToken token) { Keys.Add(key); click(key, duration, token); }
        public void PressButtons(GamePadKey key) { }
        public void ReleaseButtons(GamePadKey key) { }
        public void ClickStick(GamePadKey key, byte x, byte y, int duration, CancellationToken token) { }
        public void SetStick(GamePadKey key, byte x, byte y) { }
        public void ChangeAmiibo(uint index) { }
    }

    private sealed class RecordingConsole : IConsoleIo
    {
        public ConcurrentQueue<string> Lines { get; } = new();
        public void Print(string message, bool newline = true) => Lines.Enqueue(message);
        public void Alert(string message) => Lines.Enqueue(message);
    }
}