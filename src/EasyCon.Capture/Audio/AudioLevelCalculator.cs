using System.Buffers.Binary;

namespace EasyCon.Capture.Audio;

public static class AudioLevelCalculator
{
    public static AudioLevel Calculate(ReadOnlySpan<byte> pcm, AudioFormat format)
    {
        if (format.SampleRate <= 0 || format.Channels <= 0 || format.BitsPerSample is not (8 or 16 or 24 or 32))
            return new AudioLevel(0, 0, true);

        if (format.Encoding == "float" && format.BitsPerSample != 32)
            return new AudioLevel(0, 0, true);

        int sampleBytes = format.BitsPerSample / 8;
        if (sampleBytes <= 0 || pcm.Length < sampleBytes)
            return new AudioLevel(0, 0, true);

        double square = 0;
        double peak = 0;
        int sampleCount = 0;
        for (int offset = 0; offset + sampleBytes <= pcm.Length; offset += sampleBytes)
        {
            var value = format.Encoding switch
            {
                "float" when format.BitsPerSample == 32 => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(pcm[offset..])),
                "pcm" when format.BitsPerSample == 8 => (pcm[offset] - 128) / 128d,
                "pcm" when format.BitsPerSample == 16 => BinaryPrimitives.ReadInt16LittleEndian(pcm[offset..]) / 32768d,
                "pcm" when format.BitsPerSample == 24 => ReadPcm24(pcm[offset..]) / 8388608d,
                "pcm" when format.BitsPerSample == 32 => BinaryPrimitives.ReadInt32LittleEndian(pcm[offset..]) / 2147483648d,
                _ => 0,
            };

            if (!double.IsFinite(value))
                value = 0;
            value = Math.Clamp(value, -1, 1);
            var absolute = Math.Abs(value);
            peak = Math.Max(peak, absolute);
            square += value * value;
            sampleCount++;
        }

        var rms = sampleCount == 0 ? 0 : Math.Sqrt(square / sampleCount);
        return new AudioLevel(peak, rms, peak < 0.00001);
    }

    private static int ReadPcm24(ReadOnlySpan<byte> value)
    {
        var result = value[0] | (value[1] << 8) | (value[2] << 16);
        return (result & 0x800000) != 0 ? result - 0x1000000 : result;
    }
}