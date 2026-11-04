using EasyCon.Capture.Audio;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;

namespace EasyCon.Tests;

[TestFixture]
public class AudioLabelTests
{
    private static readonly AudioFormat _format = new(8000, 2, 16, "pcm");

    [Test]
    public void HistoryWrapsOnFramesAndSnapshotsAreIndependent()
    {
        FakeSource source = new();
        source.Connect(_format);
        using AudioHistoryBuffer history = new(source, 0.01, () => 0);
        byte[] first = Enumerable.Range(0, 240).Select(i => (byte)i).ToArray();
        byte[] second = Enumerable.Range(0, 200).Select(i => (byte)(i + 13)).ToArray();
        source.Emit(first);
        AudioClip frozen = history.Snapshot(0.01)!;
        source.Emit(second);
        AudioClip current = history.Snapshot(0.01)!;
        Assert.Multiple(() =>
        {
            Assert.That(frozen.Pcm.ToArray(), Is.EqualTo(first));
            Assert.That(current.FrameCount, Is.EqualTo(80));
            Assert.That(current.Pcm.ToArray(), Is.EqualTo(first.Concat(second).TakeLast(320).ToArray()));
            Assert.That(history.Snapshot(0.005)!.Pcm.ToArray(), Is.EqualTo(second.TakeLast(160).ToArray()));
        });
        source.Emit(new byte[800]);
        Assert.That(history.Snapshot(0.01)!.Pcm.Length, Is.EqualTo(320), "A single oversized packet must also be bounded.");
    }

    [Test]
    public void HistoryRejectsOldSessionsStalledInputAndMalformedPackets()
    {
        FakeSource source = new();
        long clock = 0;
        using AudioHistoryBuffer history = new(source, 15, () => clock);
        source.Connect(_format);
        source.Emit(new byte[320]);
        Assert.That(history.Snapshot(), Is.Not.Null);
        clock += 2 * Stopwatch.Frequency;
        Assert.That(history.Snapshot(), Is.Null, "Stalled input is not recent audio.");
        source.Emit(new byte[160]);
        Assert.That(history.Snapshot()!.Pcm.Length, Is.EqualTo(160), "Do not join audio across a long gap.");
        AudioCaptureState old = source.State;
        source.Connect(_format);
        Assert.That(history.Snapshot(), Is.Null, "A same-format reconnect is still a new session.");
        source.Emit(new byte[80]);
        source.Notify(old with { Status = AudioCaptureStatus.Disconnected });
        Assert.That(history.Snapshot()!.Pcm.Length, Is.EqualTo(80), "A delayed old terminal event must not clear new audio.");
        source.Emit(new byte[3]);
        Assert.That(history.Snapshot(), Is.Null, "A partial frame invalidates the history.");
        source.Emit(new byte[40]);
        source.Disconnect();
        Assert.That(history.Snapshot(), Is.Null);
        history.Dispose();
        Assert.That(source.Disposed, Is.False, "History borrows the capture source.");
    }

    [Test]
    public void DelayedOldAudioCannotReplaceOrPolluteReconnectedHistory()
    {
        FakeSource source = new();
        source.Connect(_format);
        using AudioHistoryBuffer history = new(source);
        AudioCaptureState old = source.State;
        byte[] previous = Enumerable.Repeat((byte)17, 160).ToArray();
        source.Emit(previous);
        source.Connect(_format);
        source.Emit(previous, old);
        Assert.That(history.Snapshot(), Is.Null, "A delayed old packet must not seed the new connection's history.");

        byte[] current = Enumerable.Repeat((byte)42, 80).ToArray();
        source.Emit(current);
        source.Emit(previous, old);
        source.Emit([1], old);
        Assert.That(history.Snapshot()!.Pcm.ToArray(), Is.EqualTo(current),
            "Old packets must neither append to nor invalidate the current connection's history.");
    }

    [Test]
    public void SlicePreservesCompleteStereoFramesAndWaveformDoesNotCancelChannels()
    {
        byte[] pcm = new byte[8000 * 4];
        for (int index = 0; index < pcm.Length; index += 4)
        {
            pcm[index + 1] = 0x40;
            pcm[index + 3] = 0xc0;
        }
        AudioClip clip = new(_format, pcm);
        AudioClip selection = clip.Slice(0.25, 0.75);
        AudioWaveformPeak[] peaks = selection.CreateWaveform(17);
        Assert.Multiple(() =>
        {
            Assert.That(selection.FrameCount, Is.EqualTo(4000));
            Assert.That(selection.Pcm.ToArray(), Is.EqualTo(pcm[8000..24000]));
            Assert.That(peaks, Has.Length.EqualTo(17));
            Assert.That(peaks.All(p => p.Minimum == -0.5f && p.Maximum == 0.5f), Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(() => clip.Slice(0.5, 0.5));
            Assert.Throws<ArgumentOutOfRangeException>(() => clip.Slice(double.NaN, 1));
        });
    }

    [TestCase("pcm", 8)]
    [TestCase("pcm", 16)]
    [TestCase("pcm", 24)]
    [TestCase("pcm", 32)]
    [TestCase("float", 32)]
    public void LabelRoundTripPreservesFormatAndBytesAndNeverOverwrites(string encoding, int bits)
    {
        string directory = Path.Combine(Path.GetTempPath(), "easycon-audio-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            AudioFormat format = new(48000, 2, bits, encoding);
            byte[] pcm = new byte[AudioClip.GetBlockAlign(format) * 1000];
            new Random(71).NextBytes(pcm);
            AudioClip clip = new(format, pcm);
            string path = AudioLabelStore.Save(directory, "闪光", clip);
            AudioLabel loaded = AudioLabelStore.Load(path);
            Assert.Multiple(() =>
            {
                Assert.That(loaded.Clip.Format, Is.EqualTo(format));
                Assert.That(loaded.Clip.Pcm.ToArray(), Is.EqualTo(pcm));
                Assert.That(loaded.Metadata.Name, Is.EqualTo("闪光"));
                Assert.Throws<IOException>(() => AudioLabelStore.Save(directory, "闪光", new AudioClip(format, new byte[pcm.Length])));
                Assert.That(AudioLabelStore.Load(path).Clip.Pcm.ToArray(), Is.EqualTo(pcm));
                Assert.That(Directory.GetDirectories(directory), Has.Length.EqualTo(1));
            });
            File.WriteAllText(path, JsonSerializer.Serialize(loaded.Metadata with { FrameCount = 1 }));
            Assert.Throws<InvalidDataException>(() => AudioLabelStore.Load(path));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [TestCase("../clip")]
    [TestCase("a\\b")]
    [TestCase("CON")]
    [TestCase("lpt1.wav")]
    [TestCase("name.")]
    [TestCase("name ")]
    [TestCase("bad:name")]
    [TestCase("")]
    public void InvalidLabelNamesCannotBecomePaths(string name)
    {
        Assert.That(AudioLabelStore.IsValidName(name), Is.False);
    }

    [TestCase("pcm", 8, 1)]
    [TestCase("pcm", 16, 2)]
    [TestCase("pcm", 24, 6)]
    [TestCase("pcm", 32, 2)]
    [TestCase("float", 32, 2)]
    public void ExtensibleWaveImportsPreservePcmAndFloatSamples(string encoding, int bits, int channels)
    {
        string path = Path.Combine(Path.GetTempPath(), "easycon-extensible-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            AudioFormat format = new(48000, channels, bits, encoding);
            byte[] pcm = new byte[AudioClip.GetBlockAlign(format) * 128];
            new Random(71).NextBytes(pcm);
            Guid subtype = new(encoding == "pcm" ? "00000001-0000-0010-8000-00aa00389b71" : "00000003-0000-0010-8000-00aa00389b71");
            WriteExtensibleWave(path, format, pcm, subtype);
            AudioClip clip = AudioLabelStore.ReadWave(path);
            Assert.Multiple(() =>
            {
                Assert.That(clip.Format, Is.EqualTo(format));
                Assert.That(clip.FrameCount, Is.EqualTo(128));
                Assert.That(clip.Pcm.ToArray(), Is.EqualTo(pcm), "Import must preserve raw samples without converting the container bit depth.");
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ExtensibleWaveRejectsUnsupportedSubtypeOrTruncatedExtension(bool truncated)
    {
        string path = Path.Combine(Path.GetTempPath(), "easycon-invalid-wave-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            Guid subtype = truncated ? new Guid("00000001-0000-0010-8000-00aa00389b71") : Guid.Empty;
            WriteExtensibleWave(path, new AudioFormat(48000, 2, 16, "pcm"), new byte[512], subtype, truncated ? 20 : 22);
            Assert.Throws<InvalidDataException>(() => AudioLabelStore.ReadWave(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void WriteExtensibleWave(string path, AudioFormat format, byte[] pcm, Guid subtype, int extensionLength = 22)
    {
        // A standard RIFF/WAVE extensible fmt chunk, independent of the label writer's output format.
        int alignment = AudioClip.GetBlockAlign(format);
        Span<byte> extension = stackalloc byte[22];
        extension.Clear();
        BinaryPrimitives.WriteUInt16LittleEndian(extension, (ushort)format.BitsPerSample);
        subtype.TryWriteBytes(extension[6..]);
        using BinaryWriter writer = new(File.Create(path));
        writer.Write("RIFF"u8);
        writer.Write(4 + 8 + 18 + extensionLength + 8 + pcm.Length);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(18 + extensionLength);
        writer.Write((ushort)0xfffe);
        writer.Write((ushort)format.Channels);
        writer.Write(format.SampleRate);
        writer.Write(format.SampleRate * alignment);
        writer.Write((ushort)alignment);
        writer.Write((ushort)format.BitsPerSample);
        writer.Write((ushort)extensionLength);
        writer.Write(extension[..extensionLength]);
        writer.Write("data"u8);
        writer.Write(pcm.Length);
        writer.Write(pcm);
    }

    [Test]
    public void FloatWaveformIgnoresNonFiniteSamplesAndClampsOutOfRange()
    {
        float[] values = [float.NaN, float.PositiveInfinity, 2f, -2f];
        byte[] pcm = new byte[16];
        Buffer.BlockCopy(values, 0, pcm, 0, pcm.Length);
        AudioClip clip = new(new AudioFormat(8000, 1, 32, "float"), pcm);
        Assert.That(clip.CreateWaveform(1), Is.EqualTo(new[] { new AudioWaveformPeak(-1, 1) }));
    }

    private sealed class FakeSource : IAudioCaptureSource
    {
        public bool IsSupported => true;
        public AudioCaptureState State { get; private set; } = new(AudioCaptureStatus.Disconnected);
        public bool Disposed { get; private set; }
        public event Action<AudioCaptureState>? StateChanged;
        public event Action<AudioChunk>? DataAvailable;
        public event Action<AudioLevel>? LevelChanged { add { } remove { } }
        public void Connect(AudioFormat format) => State = new(AudioCaptureStatus.Connected, Format: format);
        public void Emit(byte[] pcm, AudioCaptureState? session = null)
            => DataAvailable?.Invoke(new AudioChunk(0, (session ?? State).Format!, pcm, pcm.Length) { Session = session ?? State });
        public void Notify(AudioCaptureState state) => StateChanged?.Invoke(state);
        public void Disconnect() { State = new(AudioCaptureStatus.Disconnected); Notify(State); }
        public IReadOnlyList<AudioDeviceInfo> GetAvailableDevices() => [];
        public bool TryConnect(string deviceId, CancellationToken cancellationToken = default) => false;
        public Task DisconnectAsync() { Disconnect(); return Task.CompletedTask; }
        public Task<bool> DisconnectIfConnectingAsync() => Task.FromResult(false);
        public void Dispose() => Disposed = true;
    }
}