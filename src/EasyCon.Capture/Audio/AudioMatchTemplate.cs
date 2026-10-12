using NAudio.Dsp;
using System.Buffers.Binary;
using System.Numerics;
using Complex = NAudio.Dsp.Complex;

namespace EasyCon.Capture.Audio;

/// <summary>A spectral reference for diagnostic matching, not a calibrated classifier.</summary>
public sealed class AudioMatchTemplate
{
    public const double MinimumSeconds = 0.15;
    public const double MaximumSeconds = 10;

    private AudioMatchTemplate(float[][] frames, double durationSeconds)
    {
        Frames = frames;
        DurationSeconds = durationSeconds;
        SignalFrames = frames.Count(frame => frame.Any(value => value != 0));
    }

    internal float[][] Frames { get; }
    internal int SignalFrames { get; }
    public double DurationSeconds { get; }

    /// <summary>Returns null for silence or an otherwise featureless reference.</summary>
    public static AudioMatchTemplate? Create(AudioClip clip, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clip);
        if (clip.DurationSeconds < MinimumSeconds || clip.DurationSeconds > MaximumSeconds)
            throw new ArgumentOutOfRangeException(nameof(clip), "A matching reference must last 0.15 to 10 seconds.");
        List<float[]> frames = [];
        AudioSpectralFeatures extractor = new(clip.Format);
        extractor.Append(clip.Pcm.Span, frames.Add, cancellationToken);
        AudioMatchTemplate template = new(frames.ToArray(), clip.DurationSeconds);
        return template.SignalFrames >= 3 ? template : null;
    }
}

/// <summary>
/// Stateful managed resampling and STFT: 8 kHz, 32 ms Hann windows, 10 ms hops.
/// Combine channel power before normalization, so opposite stereo phases cannot cancel.
/// </summary>
internal sealed class AudioSpectralFeatures
{
    internal const int SampleRate = 8000;
    internal const int WindowSize = 256;
    internal const int HopSize = 80;
    private const int FirstBin = 3;
    internal const int FeatureCount = (WindowSize / 2) + 1 - FirstBin;
    private const int InputBatchFrames = 1024;
    private readonly AudioFormat _format;
    private readonly WdlResampler _resampler = new();
    private readonly float[] _output;
    private readonly float[] _window;
    private readonly Complex[] _fft = new Complex[WindowSize];
    private readonly double[] _power = new double[FeatureCount];
    private static readonly float[] _hann = Enumerable.Range(0, WindowSize)
        .Select(index => (float)(0.5 - (0.5 * Math.Cos(2 * Math.PI * index / (WindowSize - 1))))).ToArray();
    private int _filled;
    private long _inputFrames;
    private long _outputFrames;

    public AudioSpectralFeatures(AudioFormat format)
    {
        AudioClip.GetBlockAlign(format);
        _format = format;
        _window = new float[WindowSize * format.Channels];
        _output = new float[(InputBatchFrames + WindowSize) * format.Channels];
        _resampler.SetMode(true, 2, false);
        _resampler.SetFilterParms();
        _resampler.SetFeedMode(true);
        _resampler.SetRates(format.SampleRate, SampleRate);
    }

    public void Append(ReadOnlySpan<byte> pcm, Action<float[]> onFrame, CancellationToken cancellationToken)
    {
        int alignment = AudioClip.GetBlockAlign(_format);
        if (pcm.Length == 0 || pcm.Length % alignment != 0)
            throw new InvalidDataException("Audio contains incomplete sample frames.");
        int sampleBytes = _format.BitsPerSample / 8;
        while (!pcm.IsEmpty)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int frames = Math.Min(InputBatchFrames, pcm.Length / alignment);
            int requested = _resampler.ResamplePrepare(frames, _format.Channels, out float[] input, out int offset);
            if (requested != frames)
                throw new InvalidOperationException("Unexpected resampler input size.");
            for (int sample = 0; sample < frames * _format.Channels; sample++)
                input[offset + sample] = ReadSample(pcm[(sample * sampleBytes)..]);
            _inputFrames += frames;
            // Limit output by the cumulative sample clock. Requesting the whole output buffer
            // lets WDL step beyond a downsampled packet and lose its integer phase at the boundary.
            int wanted = (int)((_inputFrames * SampleRate / _format.SampleRate) - _outputFrames);
            int produced = _resampler.ResampleOut(_output, 0, frames, wanted, _format.Channels);
            _outputFrames += produced;
            for (int frame = 0; frame < produced; frame++)
            {
                _output.AsSpan(frame * _format.Channels, _format.Channels).CopyTo(_window.AsSpan(_filled * _format.Channels));
                _filled++;
                if (_filled != WindowSize)
                    continue;
                cancellationToken.ThrowIfCancellationRequested();
                onFrame(CreateFeature());
                _window.AsSpan(HopSize * _format.Channels).CopyTo(_window);
                _filled -= HopSize;
            }
            pcm = pcm[(frames * alignment)..];
        }
    }

    private float ReadSample(ReadOnlySpan<byte> sample)
    {
        double value = _format.Encoding == "float"
            ? BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(sample))
            : _format.BitsPerSample switch
            {
                8 => (sample[0] - 128) / 128d,
                16 => BinaryPrimitives.ReadInt16LittleEndian(sample) / 32768d,
                24 => ((sample[0] | (sample[1] << 8) | (sample[2] << 16)) << 8 >> 8) / 8388608d,
                32 => BinaryPrimitives.ReadInt32LittleEndian(sample) / 2147483648d,
                _ => 0,
            };
        if (!double.IsFinite(value))
            throw new InvalidDataException("Audio contains non-finite samples.");
        return (float)Math.Clamp(value, -1, 1);
    }

    private float[] CreateFeature()
    {
        Array.Clear(_power);
        double energy = 0;
        for (int channel = 0; channel < _format.Channels; channel++)
        {
            double channelMean = 0;
            for (int index = 0; index < WindowSize; index++)
                channelMean += _window[(index * _format.Channels) + channel];
            channelMean /= WindowSize;
            for (int index = 0; index < WindowSize; index++)
            {
                float value = _window[(index * _format.Channels) + channel] - (float)channelMean;
                energy += value * value;
                _fft[index] = new Complex { X = value * _hann[index] };
            }
            FastFourierTransform.FFT(true, 8, _fft);
            for (int bin = FirstBin; bin <= WindowSize / 2; bin++)
                _power[bin - FirstBin] += (_fft[bin].X * _fft[bin].X) + (_fft[bin].Y * _fft[bin].Y);
        }
        float[] feature = new float[FeatureCount];
        double maximum = _power.Max();
        // Gate silence before normalization: numerical noise must not become a loud fingerprint.
        if (energy / _window.Length < 1e-8 || maximum < 1e-12)
            return feature;
        double mean = 0;
        for (int bin = 0; bin < FeatureCount; bin++)
        {
            feature[bin] = (float)Math.Log(1 + (100 * Math.Sqrt(_power[bin] / maximum)));
            mean += feature[bin];
        }
        mean /= FeatureCount;
        double norm = 0;
        for (int bin = 0; bin < FeatureCount; bin++)
        {
            feature[bin] -= (float)mean;
            norm += feature[bin] * feature[bin];
        }
        if (norm < 1e-8)
            Array.Clear(feature);
        else
        {
            float scale = (float)(1 / Math.Sqrt(norm));
            for (int bin = 0; bin < FeatureCount; bin++)
                feature[bin] *= scale;
        }
        return feature;
    }
}

internal readonly record struct AudioMatchScore(double Score, double EndSeconds);

/// <summary>One incremental matcher per continuous capture session; no unbounded audio history.</summary>
internal sealed class AudioTemplateMatcher
{
    private readonly AudioMatchTemplate _template;
    private readonly AudioSpectralFeatures _extractor;
    private readonly float[][] _frames;
    private int _write;
    private long _count;

    public AudioTemplateMatcher(AudioMatchTemplate template, AudioFormat format)
    {
        _template = template;
        _extractor = new AudioSpectralFeatures(format);
        _frames = new float[template.Frames.Length][];
    }

    public void Append(ReadOnlySpan<byte> pcm, Action<AudioMatchScore> onScore, CancellationToken cancellationToken = default)
    {
        _extractor.Append(pcm, frame =>
        {
            _frames[_write] = frame;
            _write = (_write + 1) % _frames.Length;
            _count++;
            if (_count < _frames.Length)
                return;
            double sum = 0;
            for (int index = 0; index < _frames.Length; index++)
                sum += Dot(_frames[(_write + index) % _frames.Length], _template.Frames[index]);
            double score = Math.Clamp(sum / _template.SignalFrames, 0, 1);
            double end = (((_count - 1) * AudioSpectralFeatures.HopSize) + AudioSpectralFeatures.WindowSize)
                / (double)AudioSpectralFeatures.SampleRate;
            onScore(new AudioMatchScore(score, end));
        }, cancellationToken);
    }

    private static float Dot(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        Vector<float> total = Vector<float>.Zero;
        int index = 0;
        for (; index <= left.Length - Vector<float>.Count; index += Vector<float>.Count)
            total += new Vector<float>(left[index..]) * new Vector<float>(right[index..]);
        float sum = Vector.Sum(total);
        for (; index < left.Length; index++)
            sum += left[index] * right[index];
        return sum;
    }
}