using System.Runtime.InteropServices;

namespace EasyCon2.Avalonia.DrawingBoard;

/// <summary>
/// 绘制期间阻止系统空闲休眠、熄屏与屏幕保护，并声明为对时延敏感的任务。
/// macOS 进入屏幕保护后会降低程序的计时精度（App Nap 等），实测每次等待平均多出 20 毫秒以上，
/// 一张预计 47 分钟的图画了 66 分钟；Windows 也可能在无人操作时熄屏或休眠。
/// 尽力而为：系统接口不可用或调用失败时不影响绘制。其它平台不做处理。
/// </summary>
internal sealed class KeepAwake : IDisposable
{
    private readonly Action? _release;

    private KeepAwake(Action? release) => _release = release;

    /// <summary>
    /// 开始阻止休眠，释放时恢复。Windows 上的设置按线程生效，必须在同一线程上获取与释放
    /// （<see cref="DeviceDrawingRunner.Run"/> 同步执行整段绘制，满足这一点）。
    /// </summary>
    public static KeepAwake Acquire()
    {
        try
        {
            if (OperatingSystem.IsMacOS())
                return new KeepAwake(MacActivity.Begin("EasyCon drawing board"));
            if (OperatingSystem.IsWindows())
            {
                SetThreadExecutionState(EsContinuous | EsSystemRequired | EsDisplayRequired);
                return new KeepAwake(() => SetThreadExecutionState(EsContinuous));
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
        return new KeepAwake(null);
    }

    public void Dispose()
    {
        try
        {
            _release?.Invoke();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;
    private const uint EsDisplayRequired = 0x00000002;

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);

    /// <summary>经 Objective-C 运行时调用 <c>NSProcessInfo.beginActivityWithOptions:reason:</c> / <c>endActivity:</c>。</summary>
    private static class MacActivity
    {
        private const string ObjC = "/usr/lib/libobjc.A.dylib";

        // NSActivityOptions：UserInitiated（含 IdleSystemSleepDisabled）| IdleDisplaySleepDisabled | LatencyCritical
        private const ulong UserInitiated = 0x00FFFFFFUL;
        private const ulong IdleDisplaySleepDisabled = 1UL << 40;
        private const ulong LatencyCritical = 0xFF00000000UL;

        public static Action? Begin(string reason)
        {
            IntPtr pool = objc_autoreleasePoolPush();
            try
            {
                IntPtr processInfo = Send(objc_getClass("NSProcessInfo"), sel_registerName("processInfo"));
                IntPtr text = Send(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), reason);
                IntPtr activity = Send(processInfo, sel_registerName("beginActivityWithOptions:reason:"),
                    UserInitiated | IdleDisplaySleepDisabled | LatencyCritical, text);
                if (activity == IntPtr.Zero)
                    return null;
                // 返回值是自动释放的，持有到 endActivity 之后
                Send(activity, sel_registerName("retain"));
                return () => End(processInfo, activity);
            }
            finally
            {
                objc_autoreleasePoolPop(pool);
            }
        }

        private static void End(IntPtr processInfo, IntPtr activity)
        {
            IntPtr pool = objc_autoreleasePoolPush();
            try
            {
                Send(processInfo, sel_registerName("endActivity:"), activity);
                Send(activity, sel_registerName("release"));
            }
            finally
            {
                objc_autoreleasePoolPop(pool);
            }
        }

        [DllImport(ObjC)]
        private static extern IntPtr objc_getClass(string name);

        [DllImport(ObjC)]
        private static extern IntPtr sel_registerName(string name);

        [DllImport(ObjC)]
        private static extern IntPtr objc_autoreleasePoolPush();

        [DllImport(ObjC)]
        private static extern void objc_autoreleasePoolPop(IntPtr pool);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr argument);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        private static extern IntPtr Send(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string argument);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        private static extern IntPtr Send(IntPtr receiver, IntPtr selector, ulong options, IntPtr reason);
    }
}