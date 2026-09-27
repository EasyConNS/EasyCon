using Avalonia.Controls;
using Avalonia.Media;
using EasyCon.Core.Config;
using EasyCon.Core.Input;
using EasyCon.SDLInput;
using EasyCon2.Avalonia.Core.Services;
using EasyCon2.Avalonia.VPad;
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
        ConfigManager.KeyMappingChanged += OnKeyMappingChanged;

        _eventLoop.Start();
        _detector.OpenExisting();
    }

    /// <summary>键盘控制源的稳定 id（下拉显示名仍是"键盘"）。</summary>
    public const string KeyboardSourceId = "keyboard";
    private const string _gamepadIdPrefix = "gamepad:";

    public IReadOnlyList<ControlSourceInfo> GetAvailableSources()
    {
        var sources = new List<ControlSourceInfo> { new("键盘", KeyboardSourceId) };
        foreach (var (id, name) in _detector.ConnectedGamepads)
            sources.Add(new ControlSourceInfo($"手柄: {name}", $"{_gamepadIdPrefix}{id}"));
        return sources;
    }

    public bool TryConnect(string sourceId)
    {
        // 先清理上一连接的 binder 与事件订阅：任何路径重复进入 TryConnect
        // （连接进行中二次点击、切换控制源）都不会泄漏旧 binder 或重复订阅
        CleanupBinder();

        IInputBinder binder;

        if (sourceId == KeyboardSourceId)
        {
            _keyboardBinder = new SdlKeyboardInputBinder(_eventLoop, _gamepad);
            KeyMappingConfig mapping = KeyMappingStore.Instance.Current;
            _keyboardBinder.UpdateKeyMapping(mapping);
            binder = _keyboardBinder;
        }
        else if (sourceId.StartsWith(_gamepadIdPrefix, StringComparison.Ordinal))
        {
            if (!int.TryParse(sourceId[_gamepadIdPrefix.Length..], out var gpId)) return false;
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
        ConfigManager.KeyMappingChanged -= OnKeyMappingChanged;
        Disconnect();
        _eventLoop.Stop();
        _detector.Dispose();
    }

    private void OnKeyMappingChanged()
    {
        if (_keyboardBinder != null)
            _keyboardBinder.UpdateKeyMapping(KeyMappingStore.Instance.Current);
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