using Avalonia.Controls;
using Avalonia.Threading;
using EasyCon.Core.Config;
using EasyCon.Core.Input;
using EasyDevice;

namespace EasyCon2.Avalonia.Core.VPad;

public class VPadService
{
    private VPadOverlay? _overlay;
    private IInputBinder? _binder;
    private readonly NintendoSwitch _gamepad;
    private readonly IControllerAdapter _adapter;
    private bool _active;
    private Func<bool>? _escKeyDown;
    private Func<bool>? _escKeyUp;
    private Window? _owner;

    public VPadService(NintendoSwitch gamepad, IControllerAdapter adapter)
    {
        _gamepad = gamepad;
        _adapter = adapter;
    }

    public bool IsActive => _active;
    public event Action? Exited;
    public event Action<int, bool>? OverlayKeyEvent;

    private bool Active
    {
        get => _active;
        set
        {
            _active = value;
            _binder?.SetEnabled(value);
            if (_overlay != null)
                Dispatcher.UIThread.Post(() => _overlay?.IsActive = value);
        }
    }

    public void SetOwner(Window owner) => _owner = owner;

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
            _overlay.HideRequested += Exit;
            _overlay.KeyEvent += (sc, down) => OverlayKeyEvent?.Invoke(sc, down);
            _overlay.Closed += (_, _) =>
            {
                Active = false;
                _overlay = null;
            };
            _overlay.IsActive = Active;
            if (_owner != null)
                _overlay.Show(_owner);
            else
                _overlay.Show();
        });
    }

    public void SwitchInput(IInputBinder binder)
    {
        _binder?.Stop();
        _binder = binder;
        _binder.Start();
        if (_active) _binder.SetEnabled(true);
        if (_escKeyDown != null)
            _binder.RegisterEscapeKey(_escKeyDown, _escKeyUp!);
    }

    public void UpdateKeyMapping(KeyMappingConfig mapping)
    {
        _binder?.UpdateKeyMapping(mapping);
    }

    public void RegisterEscapeKey(Func<bool> keydown, Func<bool> keyup)
    {
        _escKeyDown = keydown;
        _escKeyUp = keyup;
        _binder?.RegisterEscapeKey(keydown, keyup);
    }

    public void Exit()
    {
        Active = false;
        if (_overlay != null)
        {
            if (Dispatcher.UIThread.CheckAccess())
                _overlay.Close();
            else
                Dispatcher.UIThread.Post(() => _overlay?.Close());
        }
        Exited?.Invoke();
    }

    public void Deactivate()
    {
        if (Active)
            Active = false;
    }
}