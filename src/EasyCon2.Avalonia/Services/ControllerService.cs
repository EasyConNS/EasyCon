using Avalonia.Controls;
using Avalonia.Media;
using EasyCon.Core.Input;
using EasyCon.SDLInput;
using EasyCon2.Avalonia.Core.VPad;
using EasyDevice;
using SDL;

namespace EasyCon2.Avalonia.Services;

public sealed class ControllerService : IControllerService
{
    private readonly SdlEventLoop _eventLoop;
    private readonly SdlGamepadDetector _detector;
    private readonly NintendoSwitch _gamepad;
    private readonly VPadService _vpadService;
    private readonly ControllerAdapter _adapter;
    private bool _isConnected;
    private SdlKeyboardInputBinder? _keyboardBinder;
    private SdlGamepadInputBinder? _gamepadBinder;

    public bool IsConnected => _isConnected;
    public event Action? AvailableSourcesChanged;
    public event Action? Disconnected;

    public void SetOwnerWindow(Window owner) => _vpadService.SetOwner(owner);

    public ControllerService(NintendoSwitch gamepad, IScriptService scriptService)
    {
        _gamepad = gamepad;
        _adapter = new ControllerAdapter(scriptService);
        _eventLoop = new SdlEventLoop();
        _detector = new SdlGamepadDetector();
        _vpadService = new VPadService(gamepad, _adapter);

        _eventLoop.GamepadDeviceEvent += ev => _detector.HandleDeviceEvent(ev);
        _detector.GamepadConnected += (_, _) => AvailableSourcesChanged?.Invoke();
        _detector.GamepadDisconnected += (_) => AvailableSourcesChanged?.Invoke();
        _vpadService.OverlayKeyEvent += OnOverlayKeyEvent;
        _vpadService.Exited += OnVpadExited;

        _eventLoop.Start();
        _detector.OpenExisting();
    }

    public string[] GetAvailableSources()
    {
        var sources = new List<string> { "键盘" };
        foreach (var (id, name) in _detector.ConnectedGamepads)
            sources.Add($"手柄: {name} ({id})");
        return sources.ToArray();
    }

    public bool TryConnect(string sourceName)
    {
        IInputBinder binder;

        if (sourceName == "键盘")
        {
            _keyboardBinder = new SdlKeyboardInputBinder(_eventLoop, _gamepad);
            var mapping = SdlKeyMappingDefaults.Create();
            _keyboardBinder.UpdateKeyMapping(mapping);
            binder = _keyboardBinder;
        }
        else if (sourceName.StartsWith("手柄: "))
        {
            var parenStart = sourceName.LastIndexOf('(');
            var parenEnd = sourceName.LastIndexOf(')');
            if (parenStart < 0 || parenEnd < 0) return false;
            var idStr = sourceName.Substring(parenStart + 1, parenEnd - parenStart - 1);
            if (!int.TryParse(idStr, out var gpId)) return false;
            if (!_detector.HasGamepad(gpId)) return false;

            _gamepadBinder = new SdlGamepadInputBinder(_eventLoop, _detector, _gamepad, gpId);
            binder = _gamepadBinder;
        }
        else
        {
            return false;
        }

        _vpadService.SwitchInput(binder);
        _vpadService.Show();
        _vpadService.RegisterEscapeKey(() =>
        {
            Disconnect();
            return true;
        }, () => true);

        if (_gamepadBinder != null)
        {
            _eventLoop.GamepadAxisEvent += OnGamepadAxisEvent;
            _eventLoop.GamepadButtonEvent += OnGamepadButtonEvent;
        }

        _isConnected = true;
        return true;
    }

    public void Disconnect()
    {
        if (!_isConnected) return;
        CleanupBinder();
        _vpadService.Exit();
        _isConnected = false;
    }

    private void CleanupBinder()
    {
        if (_gamepadBinder != null)
        {
            _eventLoop.GamepadAxisEvent -= OnGamepadAxisEvent;
            _eventLoop.GamepadButtonEvent -= OnGamepadButtonEvent;
            _gamepadBinder.Dispose();
            _gamepadBinder = null;
        }
        if (_keyboardBinder != null)
        {
            _keyboardBinder.Dispose();
            _keyboardBinder = null;
        }
    }

    private void OnVpadExited()
    {
        CleanupBinder();
        _isConnected = false;
        Disconnected?.Invoke();
    }

    public void Dispose()
    {
        Disconnect();
        _eventLoop.Stop();
        _detector.Dispose();
    }

    private void OnOverlayKeyEvent(int scancode, bool down)
    {
        _keyboardBinder?.HandleKeyEvent(scancode, down);
    }

    private void OnGamepadAxisEvent(SDL_GamepadAxisEvent ev)
    {
        _gamepadBinder?.Poll();
    }

    private void OnGamepadButtonEvent(SDL_GamepadButtonEvent ev)
    {
        _gamepadBinder?.Poll();
    }

    private sealed class ControllerAdapter(IScriptService scriptService) : IControllerAdapter
    {
        public bool IsRunning() => scriptService.IsRunning;
        public Color CurrentLight => Colors.White;
    }
}