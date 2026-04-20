using System.Runtime.Versioning;
using Avalonia.Threading;
using EasyCon.Core.Config;
using EasyDevice;

namespace EasyCon2.Avalonia.Core.VPad;

[SupportedOSPlatform("windows")]
public class VPadService
{
    private VPadOverlay? _overlay;
    private readonly KeyBinder _keyBinder = new();
    private readonly NintendoSwitch _gamepad;
    private readonly IControllerAdapter _adapter;
    private bool _active;

    public VPadService(NintendoSwitch gamepad, IControllerAdapter adapter)
    {
        _gamepad = gamepad;
        _adapter = adapter;
    }

    private bool Active
    {
        get => _active;
        set
        {
            _active = value;
            _keyBinder.ControllerEnabled = value;
            if (_overlay != null)
                Dispatcher.UIThread.Post(() => _overlay.IsActive = value);
        }
    }

    public void Show()
    {
        if (_overlay != null)
        {
            Active = true;
            return;
        }

        Active = true;
        Dispatcher.UIThread.Post(() =>
        {
            _overlay = new VPadOverlay(_gamepad, _adapter);
            _overlay.ToggleRequested += () => Active = !Active;
            _overlay.HideRequested += () => Active = false;
            _overlay.Closed += (_, _) =>
            {
                Active = false;
                _overlay = null;
            };
            _overlay.IsActive = Active;
            _overlay.Show();
        });
    }

    public void UpdateKeyMapping(KeyMappingConfig mapping)
    {
        _keyBinder.RegisterAllKeys(mapping, _gamepad);
    }

    public void UnregisterAllKeys()
    {
        _keyBinder.UnregisterAllKeys();
    }
}
