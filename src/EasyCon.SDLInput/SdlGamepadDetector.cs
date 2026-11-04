using SDL;

namespace EasyCon.SDLInput;

public sealed unsafe class SdlGamepadDetector : IDisposable
{
    private readonly object _lock = new();
    private readonly Dictionary<int, IntPtr> _gamepads = [];
    private readonly Dictionary<int, string> _names = [];

    // 字典由 SDL 事件循环线程写、UI 线程读，必须各自持锁；读侧返回快照，
    // 避免 UI 枚举期间字典被插拔事件修改（InvalidOperationException/脏读）。
    public IReadOnlyDictionary<int, string> ConnectedGamepads
    {
        get
        {
            lock (_lock)
            {
                return new Dictionary<int, string>(_names);
            }
        }
    }

    public event Action<int, string>? GamepadConnected;
    public event Action<int>? GamepadDisconnected;

    public void OpenExisting()
    {
        using var ids = SDL3.SDL_GetGamepads();
        foreach (var id in ids)
        {
            TryOpenGamepad(id);
        }
    }

    public void HandleDeviceEvent(SDL_GamepadDeviceEvent ev)
    {
        var t = (uint)ev.type;
        if (t == (uint)SDL_EventType.SDL_EVENT_GAMEPAD_ADDED)
        {
            TryOpenGamepad(ev.which);
        }
        else if (t == (uint)SDL_EventType.SDL_EVENT_GAMEPAD_REMOVED)
        {
            CloseGamepad(ev.which);
        }
    }

    private void TryOpenGamepad(SDL_JoystickID instanceId)
    {
        var id = (int)instanceId;
        lock (_lock)
        {
            if (_gamepads.ContainsKey(id)) return;
        }

        var gp = SDL3.SDL_OpenGamepad(instanceId);
        if (gp is null) return;

        var name = SDL3.SDL_GetGamepadName(gp) ?? "Unknown Gamepad";
        lock (_lock)
        {
            // 双重检查：并发打开（热插拔事件 + OpenExisting）时只保留先到者
            if (_gamepads.ContainsKey(id))
            {
                SDL3.SDL_CloseGamepad(gp);
                return;
            }
            _gamepads[id] = (IntPtr)gp;
            _names[id] = name;
        }
        GamepadConnected?.Invoke(id, name);
    }

    private void CloseGamepad(SDL_JoystickID instanceId)
    {
        var id = (int)instanceId;
        IntPtr gpPtr;
        lock (_lock)
        {
            if (!_gamepads.TryGetValue(id, out gpPtr)) return;
            _gamepads.Remove(id);
            _names.Remove(id);
        }
        SDL3.SDL_CloseGamepad((SDL_Gamepad*)gpPtr);
        GamepadDisconnected?.Invoke(id);
    }

    public bool HasGamepad(int instanceId)
    {
        lock (_lock)
        {
            return _gamepads.ContainsKey(instanceId);
        }
    }

    public SDL_Gamepad* GetGamepad(int instanceId)
    {
        lock (_lock)
        {
            if (_gamepads.TryGetValue(instanceId, out var ptr))
                return (SDL_Gamepad*)ptr;
            return null;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var ptr in _gamepads.Values)
                SDL3.SDL_CloseGamepad((SDL_Gamepad*)ptr);
            _gamepads.Clear();
            _names.Clear();
        }
    }
}