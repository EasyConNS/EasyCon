using SDL;

namespace EasyCon.SDLInput;

public sealed unsafe class SdlGamepadDetector : IDisposable
{
    private readonly Dictionary<int, IntPtr> _gamepads = [];
    private readonly Dictionary<int, string> _names = [];

    public IReadOnlyDictionary<int, string> ConnectedGamepads => _names;

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
        if (_gamepads.ContainsKey(id)) return;

        var gp = SDL3.SDL_OpenGamepad(instanceId);
        if (gp is null) return;

        var name = SDL3.SDL_GetGamepadName(gp) ?? "Unknown Gamepad";
        _gamepads[id] = (IntPtr)gp;
        _names[id] = name;
        GamepadConnected?.Invoke(id, name);
    }

    private void CloseGamepad(SDL_JoystickID instanceId)
    {
        var id = (int)instanceId;
        if (_gamepads.TryGetValue(id, out var gpPtr))
        {
            SDL3.SDL_CloseGamepad((SDL_Gamepad*)gpPtr!);
            _gamepads.Remove(id);
            _names.Remove(id);
            GamepadDisconnected?.Invoke(id);
        }
    }

    public bool HasGamepad(int instanceId) => _gamepads.ContainsKey(instanceId);

    public SDL_Gamepad* GetGamepad(int instanceId)
    {
        if (_gamepads.TryGetValue(instanceId, out var ptr))
            return (SDL_Gamepad*)ptr;
        return null;
    }

    public void Dispose()
    {
        foreach (var ptr in _gamepads.Values)
            SDL3.SDL_CloseGamepad((SDL_Gamepad*)ptr);
        _gamepads.Clear();
        _names.Clear();
    }
}