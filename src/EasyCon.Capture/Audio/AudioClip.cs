using System.Buffers.Binary;

namespace EasyCon.Capture.Audio;

public readonly record struct AudioWaveformPeak(float Minimum, float Maximum);

/// <summary>An owned, frame-aligned snapshot. Editing never changes live capture data.</summary>
public sealed class AudioClip
{
    public const int MaximumBytes = 16 * 1024 * 1024;
    private readonly byte[] _pcm;

    public AudioClip(AudioFormat format, ReadOnlySpan<byte> pcm)
    {
        int alignment = GetBlockAlign(format);
        if (pcm.Length == 0 || pcm.Length > MaximumBytes || pcm.Length % alignment != 0)
            throw new ArgumentException("Audio must contain complete frames within the 16 MiB limit.", nameof(pcm));
        Format = format;
        _pcm = pcm.ToArray();
    }

    public AudioFormat Format { get; }
    public ReadOnlyMemory<byte> Pcm => _pcm;
    public int FrameCount => _pcm.Length / GetBlockAlign(Format);
    public double DurationSeconds => FrameCount / (double)Format.SampleRate;

    public static int GetBlockAlign(AudioFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (format.SampleRate is < 8000 or > 384000 || format.Channels is < 1 or > 32
            || (format.Encoding == "pcm" && format.BitsPerSample is not (8 or 16 or 24 or 32))
            || (format.Encoding == "float" && format.BitsPerSample != 32)
            || format.Encoding is not ("pcm" or "float"))
            throw new ArgumentException("Unsupported audio format.", nameof(format));
        return format.Channels * (format.BitsPerSample / 8);
    }

    public AudioClip Slice(double startSeconds, double endSeconds)
    {
        if (!double.IsFinite(startSeconds) || !double.IsFinite(endSeconds)
            || startSeconds < 0 || endSeconds > DurationSeconds || endSeconds <= startSeconds)
            throw new ArgumentOutOfRangeException(nameof(startSeconds));
        int start = Math.Clamp((int)Math.Floor(startSeconds * Format.SampleRate), 0, FrameCount - 1);
        int end = Math.Clamp((int)Math.Ceiling(endSeconds * Format.SampleRate), start + 1, FrameCount);
        int alignment = GetBlockAlign(Format);
        return new AudioClip(Format, _pcm.AsSpan(start * alignment, (end - start) * alignment));
    }

    public AudioWaveformPeak[] CreateWaveform(int maxBins = 1200)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBins);
        int count = Math.Min(maxBins, FrameCount);
        AudioWaveformPeak[] peaks = new AudioWaveformPeak[count];
        int sampleBytes = Format.BitsPerSample / 8;
        for (int bin = 0; bin < count; bin++)
        {
            int start = (int)((long)bin * FrameCount / count) * Format.Channels;
            int end = (int)((long)(bin + 1) * FrameCount / count) * Format.Channels;
            float minimum = 0;
            float maximum = 0;
            for (int sample = start; sample < end; sample++)
            {
                float value = ReadSample(_pcm.AsSpan(sample * sampleBytes));
                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
            }
            peaks[bin] = new AudioWaveformPeak(minimum, maximum);
        }
        return peaks;
    }

    private float ReadSample(ReadOnlySpan<byte> sample)
    {
        double value = Format.Encoding == "float"
            ? BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(sample))
            : Format.BitsPerSample switch
            {
                8 => (sample[0] - 128) / 128d,
                16 => BinaryPrimitives.ReadInt16LittleEndian(sample) / 32768d,
                24 => ((sample[0] | (sample[1] << 8) | (sample[2] << 16)) << 8 >> 8) / 8388608d,
                32 => BinaryPrimitives.ReadInt32LittleEndian(sample) / 2147483648d,
                _ => 0,
            };
        return double.IsFinite(value) ? (float)Math.Clamp(value, -1, 1) : 0;
    }
}