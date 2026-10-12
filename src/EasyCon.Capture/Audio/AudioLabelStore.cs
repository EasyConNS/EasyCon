using NAudio.Wave;
using System.Text.Json;

namespace EasyCon.Capture.Audio;

public sealed record AudioLabelMetadata(int Version, string Name, AudioFormat Format, int FrameCount, DateTimeOffset CreatedAtUtc);
public sealed record AudioLabel(AudioLabelMetadata Metadata, AudioClip Clip);

/// <summary>A movable label directory containing label.json and an unprocessed audio.wav.</summary>
public static class AudioLabelStore
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public static bool IsValidName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name != name.Trim()
            || name.EndsWith('.') || name.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c)))
            return false;
        string stem = name.Split('.')[0].ToUpperInvariant();
        return stem is not ("CON" or "PRN" or "AUX" or "NUL")
            && !(stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '0' and <= '9');
    }

    public static string Save(string parentDirectory, string name, AudioClip clip)
    {
        if (!IsValidName(name))
            throw new ArgumentException("Invalid audio label name.", nameof(name));
        ArgumentNullException.ThrowIfNull(clip);
        string parent = Path.GetFullPath(parentDirectory);
        Directory.CreateDirectory(parent);
        string target = Path.Combine(parent, name + ".audiolabel");
        if (Directory.Exists(target) || File.Exists(target))
            throw new IOException("An audio label with this name already exists.");
        string temporary = Path.Combine(parent, ".audio-label-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            using (WaveFileWriter writer = new(Path.Combine(temporary, "audio.wav"), ToWaveFormat(clip.Format)))
                writer.Write(clip.Pcm.Span);
            AudioLabelMetadata metadata = new(1, name, clip.Format, clip.FrameCount, DateTimeOffset.UtcNow);
            File.WriteAllText(Path.Combine(temporary, "label.json"), JsonSerializer.Serialize(metadata, _jsonOptions));
            // Publish both files together; collisions never overwrite an existing label.
            Directory.Move(temporary, target);
            return Path.Combine(target, "label.json");
        }
        finally
        {
            if (Directory.Exists(temporary))
                Directory.Delete(temporary, true);
        }
    }

    public static AudioLabel Load(string metadataPath)
    {
        if (new FileInfo(metadataPath).Length > 64 * 1024)
            throw new InvalidDataException("Audio label metadata is too large.");
        AudioLabelMetadata metadata = JsonSerializer.Deserialize<AudioLabelMetadata>(File.ReadAllText(metadataPath))
            ?? throw new InvalidDataException("Missing audio label metadata.");
        if (metadata.Version != 1 || !IsValidName(metadata.Name) || metadata.FrameCount <= 0)
            throw new InvalidDataException("Unsupported or invalid audio label.");
        AudioClip clip = ReadWave(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(metadataPath))!, "audio.wav"));
        if (metadata.Format != clip.Format || metadata.FrameCount != clip.FrameCount)
            throw new InvalidDataException("Audio label metadata does not match its WAV file.");
        return new AudioLabel(metadata, clip);
    }

    public static AudioClip ReadWave(string path)
    {
        using WaveFileReader reader = new(path);
        WaveFormat wave = reader.WaveFormat;
        string encoding = wave.Encoding switch
        {
            WaveFormatEncoding.Pcm => "pcm",
            WaveFormatEncoding.IeeeFloat => "float",
            _ => throw new InvalidDataException("Only PCM and float WAV files are supported."),
        };
        AudioFormat format = new(wave.SampleRate, wave.Channels, wave.BitsPerSample, encoding);
        int alignment = AudioClip.GetBlockAlign(format);
        if (reader.Length <= 0 || reader.Length > AudioClip.MaximumBytes || reader.Length % alignment != 0)
            throw new InvalidDataException("WAV must contain complete frames within the 16 MiB limit.");
        byte[] pcm = new byte[(int)reader.Length];
        reader.ReadExactly(pcm);
        return new AudioClip(format, pcm);
    }

    public static WaveFormat ToWaveFormat(AudioFormat format)
    {
        AudioClip.GetBlockAlign(format);
        return format.Encoding == "float"
            ? WaveFormat.CreateIeeeFloatWaveFormat(format.SampleRate, format.Channels)
            : new WaveFormat(format.SampleRate, format.BitsPerSample, format.Channels);
    }
}