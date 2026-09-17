using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using EasyDevice;

namespace EasyCon2.Avalonia.Core.VPad;

public partial class VPadOverlay : Window
{
    private readonly JCDrawControl _drawControl;
    private bool _mouseDown;
    private bool _mouseMoved;
    private Point _lastLocation;

    public event Action? ToggleRequested;
    public event Action? HideRequested;
    public event Action<int, bool>? KeyEvent;

    public new bool IsActive
    {
        set
        {
            IsVisible = true;
            Opacity = value ? 1.0 : 0.5;
        }
    }

    public VPadOverlay(IReporter reporter, IControllerAdapter adapter)
    {
        InitializeComponent();

        _drawControl = new JCDrawControl(reporter, adapter);
        Content = _drawControl;
        Focusable = true;

        PointerPressed += OnPointerPressed;
        PointerReleased += OnPointerReleased;
        PointerMoved += OnPointerMoved;
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;

        ResetLocation();
    }

    public void ResetLocation()
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen != null)
        {
            Position = new PixelPoint(
                screen.WorkingArea.X + screen.WorkingArea.Width / 2,
                screen.WorkingArea.Y + (screen.WorkingArea.Height - 100) / 2
            );
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            _mouseDown = true;
            _mouseMoved = false;
            _lastLocation = e.GetPosition(this);
            e.Handled = true;
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (point.Properties.PointerUpdateKind == PointerUpdateKind.RightButtonReleased)
        {
            if (!_mouseMoved)
                ResetLocation();
            _mouseDown = false;
        }
        else if (point.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonReleased)
        {
            ToggleRequested?.Invoke();
        }
        else if (point.Properties.PointerUpdateKind == PointerUpdateKind.MiddleButtonReleased)
        {
            HideRequested?.Invoke();
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_mouseDown) return;

        var pos = e.GetPosition(this);
        if (pos.X == _lastLocation.X && pos.Y == _lastLocation.Y)
            return;

        _mouseMoved = true;
        var delta = pos - _lastLocation;
        Position = new PixelPoint(Position.X + (int)delta.X, Position.Y + (int)delta.Y);
    }

    protected override void OnClosed(EventArgs e)
    {
        _drawControl.Stop();
        base.OnClosed(e);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var sc = ToSdlScancode(e.Key);
        if (sc >= 0)
        {
            KeyEvent?.Invoke(sc, true);
            e.Handled = true;
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        var sc = ToSdlScancode(e.Key);
        if (sc >= 0)
        {
            KeyEvent?.Invoke(sc, false);
            e.Handled = true;
        }
    }

    private static int ToSdlScancode(Key key)
    {
        return key switch
        {
            Key.A => 4,
            Key.B => 5,
            Key.C => 6,
            Key.D => 7,
            Key.E => 8,
            Key.F => 9,
            Key.G => 10,
            Key.H => 11,
            Key.I => 12,
            Key.J => 13,
            Key.K => 14,
            Key.L => 15,
            Key.M => 16,
            Key.N => 17,
            Key.O => 18,
            Key.P => 19,
            Key.Q => 20,
            Key.R => 21,
            Key.S => 22,
            Key.T => 23,
            Key.U => 24,
            Key.V => 25,
            Key.W => 26,
            Key.X => 27,
            Key.Y => 28,
            Key.Z => 29,
            Key.Up => 82,
            Key.Down => 81,
            Key.Left => 80,
            Key.Right => 79,
            Key.Escape => 41,
            _ => -1,
        };
    }
}