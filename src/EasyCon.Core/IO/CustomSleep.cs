using System.Diagnostics;
using System.Runtime.InteropServices;

public sealed class CustomDelay
{
    private static int CurrTimestamp => (int)(DateTime.Now.Ticks / 10_000);

    public static void Delay(int millisecondsTimeout, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var curtime = CurrTimestamp;
        if (millisecondsTimeout >= 40) Task.Delay(millisecondsTimeout - 20, token).Wait(token);
        while (!token.IsCancellationRequested)
        {
            if (CurrTimestamp - curtime >= millisecondsTimeout) break;
            Thread.Yield();
        }
    }
    /// <summary>
    /// 跨平台精确毫秒级延迟（误差通常 < 2ms）
    /// </summary>
    /// <param name="milliseconds">要延迟的毫秒数</param>
    public static void AISleep(int milliseconds)
    {
        if (milliseconds <= 0) return;

        var sw = Stopwatch.StartNew();
        long remainingTicks = milliseconds * 10000L; // Stopwatch 以 100ns 为单位

        // 如果剩余时间 > 5ms，先使用平台特定高精度睡眠
        if (remainingTicks > 50000) // 5ms
        {
            // 预留 2-3ms 给最后自旋
            int sleepMs = Math.Max(1, (int)((remainingTicks - 30000) / 10000));

            // 调用平台特定的高精度睡眠
            HighResolutionSleep(sleepMs);

            // 重新计算剩余时间
            remainingTicks = milliseconds * 10000L - sw.ElapsedTicks;
        }

        // 最后自旋微调（精度关键阶段）
        while (remainingTicks > 0)
        {
            if (remainingTicks <= 2000) // 剩余 < 0.2ms，极短自旋
            {
                Thread.SpinWait(1);
            }
            else
            {
                Thread.Yield(); // 主动让出CPU，但保持就绪状态
            }
            remainingTicks = milliseconds * 10000L - sw.ElapsedTicks;
        }
    }
    /// <summary>
    /// 平台特定的高精度睡眠实现
    /// </summary>
    private static void HighResolutionSleep(int milliseconds)
    {
        if (milliseconds <= 0) return;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows: 使用 timeBeginPeriod + Sleep
            TimeBeginPeriod(1);
            try
            {
                Thread.Sleep(milliseconds);
            }
            finally
            {
                TimeEndPeriod(1);
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                 RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // Linux/macOS: 使用 nanosleep 实现微秒级精度
            Nanosleep(milliseconds * 1_000_000L);
        }
    }

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint period);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint period);

    [DllImport("libc", SetLastError = true, EntryPoint = "nanosleep")]
    private static extern int Nanosleep(ref Timespec req, ref Timespec rem);

    private static void Nanosleep(long nanoseconds)
    {
        var req = new Timespec
        {
            tv_sec = nanoseconds / 1_000_000_000,
            tv_nsec = nanoseconds % 1_000_000_000
        };
        var rem = new Timespec();

        // nanosleep 可能被信号中断，需要循环处理
        while (nanosleep(req.tv_sec, req.tv_nsec))
        {
            req = rem;
        }
    }

    private static bool nanosleep(long sec, long nsec)
    {
        var req = new Timespec { tv_sec = sec, tv_nsec = nsec };
        var rem = new Timespec();
        int result = Nanosleep(ref req, ref rem);

        if (result == 0) return false; // 成功完成

        // 被信号中断，继续睡眠剩余时间
        req = rem;
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Timespec
    {
        public long tv_sec;   // 秒
        public long tv_nsec;  // 纳秒
    }
}