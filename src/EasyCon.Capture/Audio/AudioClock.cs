using System.Diagnostics;

namespace EasyCon.Capture.Audio;

internal static class AudioClock
{
    public static long NowNanoseconds() => ToNanoseconds(Stopwatch.GetTimestamp());

    public static long ToNanoseconds(long ticks)
        => (ticks / Stopwatch.Frequency * 1_000_000_000L)
            + (ticks % Stopwatch.Frequency * 1_000_000_000L / Stopwatch.Frequency);
}