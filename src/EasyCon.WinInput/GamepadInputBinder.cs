using EasyCon.Core.Config;
using EasyCon.Core.Input;
using EasyDevice;
using GamepadApi;
using System.Runtime.Versioning;

namespace EasyCon.WinInput;

[SupportedOSPlatform("windows")]
public class GamepadInputBinder : IInputBinder
{
    private readonly GamepadManager _manager;
    private readonly int _deviceIndex;
    private readonly NintendoSwitch _ns;
    private readonly GamepadMappingConfig _config;
    private volatile bool _enabled;

    public GamepadInputBinder(GamepadManager manager, int deviceIndex, NintendoSwitch ns, GamepadMappingConfig config)
    {
        _manager = manager;
        _deviceIndex = deviceIndex;
        _ns = ns;
        _config = config;
    }

    public void Start()
    {
        _manager.StateChanged += OnStateChanged;
    }

    public void Stop()
    {
        _manager.StateChanged -= OnStateChanged;
        _ns.Reset();
    }

    public void SetEnabled(bool enabled) => _enabled = enabled;

    public void UpdateKeyMapping(KeyMappingConfig mapping) { }

    public void RegisterEscapeKey(Func<bool> keydown, Func<bool> keyup) { }

    private void OnStateChanged(GamepadDevice device, GamepadState state)
    {
        if (!_enabled) return;
        if (device.Index != _deviceIndex) return;
        var report = GamepadMapper.Map(state, _config);
        _ns.ApplyReport(report);
    }
}