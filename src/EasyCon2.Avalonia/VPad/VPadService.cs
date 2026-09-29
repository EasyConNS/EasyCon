using Avalonia.Controls;
using Avalonia.Threading;
using EasyCon.Core.Config;
using EasyCon.Core.Input;
using EasyDevice;

namespace EasyCon2.Avalonia.VPad;

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
    /// <summary>覆盖层会话号：Show/Exit 各自递增，使在途的创建 Post 失效（跨线程调用，需 Interlocked）。</summary>
    private int _session;

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
        int session = Interlocked.Increment(ref _session);
        Dispatcher.UIThread.Post(() =>
        {
            // Exit 抢在本 Post 之前执行（或二次 Show 已开新会话）时不得再创建，
            // 否则此后 _isConnected 已为 false，没有任何路径会关闭这个孤儿覆盖层
            if (session != Volatile.Read(ref _session))
                return;

            var overlay = new VPadOverlay(_gamepad, _adapter);
            overlay.ToggleRequested += () => Active = !Active;
            overlay.HideRequested += Exit;
            overlay.KeyEvent += (sc, down) => OverlayKeyEvent?.Invoke(sc, down);
            overlay.Closed += (_, _) => OnOverlayClosed();
            overlay.IsActive = Active;
            if (_owner != null)
                overlay.Show(_owner);
            else
                overlay.Show();

            _overlay = overlay;
            // Exit 在创建过程中从其它线程插入：关掉刚显示的覆盖层（Closed → OnOverlayClosed 补发 Exited）
            if (session != Volatile.Read(ref _session))
                overlay.Close();
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
        Interlocked.Increment(ref _session);
        if (_overlay != null)
        {
            if (Dispatcher.UIThread.CheckAccess())
                _overlay.Close();
            else
                Dispatcher.UIThread.Post(() => _overlay?.Close());
            // Close 会触发 Closed → OnOverlayClosed → Exited，此处不再重复触发
            return;
        }
        Exited?.Invoke();
    }

    /// <summary>
    /// 覆盖层关闭（包括 Alt+F4 等系统级关闭，它们不走 <see cref="Exit"/>）。
    /// 统一复位状态并通知订阅方，否则主界面会停留在"已连接手柄"。
    /// </summary>
    private void OnOverlayClosed()
    {
        Active = false;
        _overlay = null;
        Exited?.Invoke();
    }

    public void Deactivate()
    {
        if (Active)
            Active = false;
    }
}