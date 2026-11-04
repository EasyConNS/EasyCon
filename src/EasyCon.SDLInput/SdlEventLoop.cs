using SDL;

namespace EasyCon.SDLInput;

public sealed class SdlEventLoop : IDisposable
{
    private Thread? _thread;
    private volatile bool _running;
    private readonly object _lock = new();

    public event Action<SDL_KeyboardEvent>? KeyEvent;
    public event Action<SDL_GamepadButtonEvent>? GamepadButtonEvent;
    public event Action<SDL_GamepadAxisEvent>? GamepadAxisEvent;
    public event Action<SDL_GamepadDeviceEvent>? GamepadDeviceEvent;

    /// <summary>
    /// 事件处理器抛出的异常经此上报（宿主接到日志）；后台线程上的未处理异常
    /// 会直接终止整个进程，因此 Dispatch 内必须兜住。未设置时静默忽略。
    /// </summary>
    public static Action<Exception>? HandlerException { get; set; }

    public void Start()
    {
        lock (_lock)
        {
            if (_running) return;
            _running = true;
            _thread = new Thread(Run) { IsBackground = true, Name = "SDL3 Event Loop" };
            _thread.Start();
        }
    }

    public void Stop()
    {
        bool wasRunning;
        lock (_lock)
        {
            wasRunning = _running;
            _running = false;
        }
        if (!wasRunning) return;

        // 有界等待：正常 1ms 轮询循环瞬间退出；若 SDL_PollEvent 卡死，
        // 不能让窗口关闭路径（UI 线程）无限冻结。线程为 IsBackground，残留无害。
        _thread?.Join(TimeSpan.FromSeconds(2));
        _thread = null;
    }

    private void Run()
    {
        // SDL_Init 在 try 内并检查返回值：原生库缺失/初始化失败时上报而非
        // 让 DllNotFoundException 变成进程级未处理异常，或失败后静默空转
        try
        {
            if (!SDL3.SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO | SDL_InitFlags.SDL_INIT_GAMEPAD))
            {
                HandlerException?.Invoke(new InvalidOperationException(
                    $"SDL_Init 失败: {SDL3.SDL_GetError()}"));
                return;
            }

            // 全局键盘捕获（SDL 3.4.4+）：Windows 下 SDL 据此以 RAWINPUT+INPUTSINK 注册，
            // 不创建窗口、不抢焦点，任意前台应用下都能收到 SDL 键盘事件，且只观察不吞键
            // （不带 NOLEGACY/NOHOTKEYS，前台应用的正常按键流程不受影响）。
            // 其它平台/旧版 SDL 会忽略这两个 hint，回落到窗口焦点路径（VPadOverlay 聚焦仍可用）。
            SDL3.SDL_SetHint(SDL3.SDL_HINT_WINDOWS_RAW_KEYBOARD, "1");
            SDL3.SDL_SetHint(SDL3.SDL_HINT_WINDOWS_RAW_KEYBOARD_INPUTSINK, "1");

            while (_running)
            {
                PollEvents();
                Thread.Sleep(1);
            }
        }
        finally
        {
            SDL3.SDL_Quit();
        }
    }

    private unsafe void PollEvents()
    {
        SDL_Event ev;
        while ((bool)SDL3.SDL_PollEvent(&ev))
        {
            Dispatch(ev);
        }
    }

    private void Dispatch(SDL_Event ev)
    {
        var t = (uint)ev.type;
        if (t == (uint)SDL_EventType.SDL_EVENT_KEY_DOWN || t == (uint)SDL_EventType.SDL_EVENT_KEY_UP)
        {
            if (!ev.key.repeat)
                SafeInvoke(KeyEvent, ev.key);
        }
        else if (t == (uint)SDL_EventType.SDL_EVENT_GAMEPAD_BUTTON_DOWN || t == (uint)SDL_EventType.SDL_EVENT_GAMEPAD_BUTTON_UP)
        {
            SafeInvoke(GamepadButtonEvent, ev.gbutton);
        }
        else if (t == (uint)SDL_EventType.SDL_EVENT_GAMEPAD_AXIS_MOTION)
        {
            SafeInvoke(GamepadAxisEvent, ev.gaxis);
        }
        else if (t == (uint)SDL_EventType.SDL_EVENT_GAMEPAD_ADDED || t == (uint)SDL_EventType.SDL_EVENT_GAMEPAD_REMOVED)
        {
            SafeInvoke(GamepadDeviceEvent, ev.gdevice);
        }
    }

    private static void SafeInvoke<T>(Action<T>? handlers, T arg)
    {
        try
        {
            handlers?.Invoke(arg);
        }
        catch (Exception ex)
        {
            HandlerException?.Invoke(ex);
        }
    }

    public void Dispose()
    {
        Stop();
    }
}