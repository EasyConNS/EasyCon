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

    public bool IsActive
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

        PointerPressed += OnPointerPressed;
        PointerReleased += OnPointerReleased;
        PointerMoved += OnPointerMoved;

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
}