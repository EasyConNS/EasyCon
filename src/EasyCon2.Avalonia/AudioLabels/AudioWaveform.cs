using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using EasyCon.Capture.Audio;

namespace EasyCon2.Avalonia.AudioLabels;

/// <summary>Waveform selection surface; numeric controls provide an equivalent keyboard path.</summary>
public sealed class AudioWaveform : Control
{
    public static readonly StyledProperty<AudioWaveformPeak[]?> PeaksProperty =
        AvaloniaProperty.Register<AudioWaveform, AudioWaveformPeak[]?>(nameof(Peaks));
    public static readonly StyledProperty<double> DurationProperty =
        AvaloniaProperty.Register<AudioWaveform, double>(nameof(Duration));
    public static readonly StyledProperty<double> SelectionStartProperty =
        AvaloniaProperty.Register<AudioWaveform, double>(nameof(SelectionStart), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<double> SelectionEndProperty =
        AvaloniaProperty.Register<AudioWaveform, double>(nameof(SelectionEnd), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<IBrush> WaveBrushProperty =
        AvaloniaProperty.Register<AudioWaveform, IBrush>(nameof(WaveBrush), Brushes.SteelBlue);

    private double _anchor;
    private bool _dragging;
    private int _handle;

    static AudioWaveform()
    {
        AffectsRender<AudioWaveform>(PeaksProperty, DurationProperty, SelectionStartProperty, SelectionEndProperty, WaveBrushProperty);
    }

    public AudioWaveformPeak[]? Peaks { get => GetValue(PeaksProperty); set => SetValue(PeaksProperty, value); }
    public double Duration { get => GetValue(DurationProperty); set => SetValue(DurationProperty, value); }
    public double SelectionStart { get => GetValue(SelectionStartProperty); set => SetValue(SelectionStartProperty, value); }
    public double SelectionEnd { get => GetValue(SelectionEndProperty); set => SetValue(SelectionEndProperty, value); }
    public IBrush WaveBrush { get => GetValue(WaveBrushProperty); set => SetValue(WaveBrushProperty, value); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Rect bounds = new(Bounds.Size);
        context.FillRectangle(Brushes.Transparent, bounds);
        if (Peaks is not { Length: > 0 } peaks || Duration <= 0 || bounds.Width <= 0)
            return;
        double middle = bounds.Height / 2;
        double scale = Math.Max(0, middle - 12);
        Pen wave = new(WaveBrush, Math.Max(1, bounds.Width / peaks.Length));
        using (context.PushClip(bounds))
        {
            double left = Math.Clamp(SelectionStart / Duration, 0, 1) * bounds.Width;
            double right = Math.Clamp(SelectionEnd / Duration, 0, 1) * bounds.Width;
            using (context.PushOpacity(0.16))
                context.FillRectangle(WaveBrush, new Rect(left, 0, Math.Max(0, right - left), bounds.Height));
            using (context.PushOpacity(0.3))
                context.DrawLine(new Pen(WaveBrush), new Point(0, middle), new Point(bounds.Width, middle));
            for (int index = 0; index < peaks.Length; index++)
            {
                double x = (index + 0.5) * bounds.Width / peaks.Length;
                context.DrawLine(wave, new Point(x, middle - peaks[index].Maximum * scale),
                    new Point(x, middle - peaks[index].Minimum * scale));
            }
            foreach (double x in new[] { left, right })
            {
                context.DrawLine(new Pen(WaveBrush, 2), new Point(x, 0), new Point(x, bounds.Height));
                context.FillRectangle(WaveBrush, new Rect(Math.Clamp(x - 4, 0, Math.Max(0, bounds.Width - 8)), 0, 8, 18), 3);
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Duration <= 0 || Bounds.Width <= 0 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        double x = e.GetPosition(this).X;
        double left = SelectionStart / Duration * Bounds.Width;
        double right = SelectionEnd / Duration * Bounds.Width;
        _handle = Math.Abs(x - left) <= 12 && Math.Abs(x - left) <= Math.Abs(x - right) ? -1
            : Math.Abs(x - right) <= 12 ? 1 : 0;
        _anchor = ToSeconds(x);
        _dragging = true;
        e.Pointer.Capture(this);
        if (_handle == 0)
            SetRange(_anchor, _anchor);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging)
            return;
        double position = ToSeconds(e.GetPosition(this).X);
        if (_handle == -1)
            SetCurrentValue(SelectionStartProperty, Math.Min(position, SelectionEnd));
        else if (_handle == 1)
            SetCurrentValue(SelectionEndProperty, Math.Max(position, SelectionStart));
        else
            SetRange(Math.Min(_anchor, position), Math.Max(_anchor, position));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragging = false;
    }

    private double ToSeconds(double x) => Math.Clamp(x / Bounds.Width, 0, 1) * Duration;

    private void SetRange(double start, double end)
    {
        SetCurrentValue(SelectionStartProperty, 0d);
        SetCurrentValue(SelectionEndProperty, end);
        SetCurrentValue(SelectionStartProperty, start);
    }
}