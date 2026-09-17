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
        lock (_lock)
        {
            if (!_running) return;
            _running = false;
        }
        _thread?.Join();
        _thread = null;
    }

    private void Run()
    {
        SDL3.SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO | SDL_InitFlags.SDL_INIT_GAMEPAD);

        try
        {
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
                KeyEvent?.Invoke(ev.key);
        }
        else if (t == (uint)SDL_EventType.SDL_EVENT_GAMEPAD_BUTTON_DOWN || t == (uint)SDL_EventType.SDL_EVENT_GAMEPAD_BUTTON_UP)
        {
            GamepadButtonEvent?.Invoke(ev.gbutton);
        }
        else if (t == (uint)SDL_EventType.SDL_EVENT_GAMEPAD_AXIS_MOTION)
        {
            GamepadAxisEvent?.Invoke(ev.gaxis);
        }
        else if (t == (uint)SDL_EventType.SDL_EVENT_GAMEPAD_ADDED || t == (uint)SDL_EventType.SDL_EVENT_GAMEPAD_REMOVED)
        {
            GamepadDeviceEvent?.Invoke(ev.gdevice);
        }
    }

    public void Dispose()
    {
        Stop();
    }
}