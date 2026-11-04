using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using EasyCon.Capture.Audio;
using System.Globalization;
using System.Windows.Input;

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
    public static readonly StyledProperty<double?> PlaybackPositionProperty =
        AvaloniaProperty.Register<AudioWaveform, double?>(nameof(PlaybackPosition));
    public static readonly StyledProperty<IBrush> PlaybackBrushProperty =
        AvaloniaProperty.Register<AudioWaveform, IBrush>(nameof(PlaybackBrush), Brushes.OrangeRed);
    public static readonly StyledProperty<IBrush> RulerBrushProperty =
        AvaloniaProperty.Register<AudioWaveform, IBrush>(nameof(RulerBrush), Brushes.Gray);
    public static readonly StyledProperty<ICommand?> SeekCommandProperty =
        AvaloniaProperty.Register<AudioWaveform, ICommand?>(nameof(SeekCommand));

    private double _anchor;
    private double _pressX;
    private bool _dragging;
    private bool _moved;
    private int _handle;

    static AudioWaveform()
    {
        AffectsRender<AudioWaveform>(PeaksProperty, DurationProperty, SelectionStartProperty, SelectionEndProperty,
            WaveBrushProperty, PlaybackPositionProperty, PlaybackBrushProperty, RulerBrushProperty);
    }

    public AudioWaveformPeak[]? Peaks { get => GetValue(PeaksProperty); set => SetValue(PeaksProperty, value); }
    public double Duration { get => GetValue(DurationProperty); set => SetValue(DurationProperty, value); }
    public double SelectionStart { get => GetValue(SelectionStartProperty); set => SetValue(SelectionStartProperty, value); }
    public double SelectionEnd { get => GetValue(SelectionEndProperty); set => SetValue(SelectionEndProperty, value); }
    public IBrush WaveBrush { get => GetValue(WaveBrushProperty); set => SetValue(WaveBrushProperty, value); }
    public double? PlaybackPosition { get => GetValue(PlaybackPositionProperty); set => SetValue(PlaybackPositionProperty, value); }
    public IBrush PlaybackBrush { get => GetValue(PlaybackBrushProperty); set => SetValue(PlaybackBrushProperty, value); }
    public IBrush RulerBrush { get => GetValue(RulerBrushProperty); set => SetValue(RulerBrushProperty, value); }
    public ICommand? SeekCommand { get => GetValue(SeekCommandProperty); set => SetValue(SeekCommandProperty, value); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Rect bounds = new(Bounds.Size);
        context.FillRectangle(Brushes.Transparent, bounds);
        if (Peaks is not { Length: > 0 } peaks || !double.IsFinite(Duration) || Duration <= 0 || bounds.Width <= 0 || bounds.Height <= 28)
            return;
        double waveHeight = bounds.Height - 28;
        double middle = waveHeight / 2;
        double scale = Math.Max(0, middle - 12);
        Pen wave = new(WaveBrush, Math.Max(1, bounds.Width / peaks.Length));
        using (context.PushClip(bounds))
        {
            double left = Math.Clamp(SelectionStart / Duration, 0, 1) * bounds.Width;
            double right = Math.Clamp(SelectionEnd / Duration, 0, 1) * bounds.Width;
            using (context.PushOpacity(0.16))
                context.FillRectangle(WaveBrush, new Rect(left, 0, Math.Max(0, right - left), waveHeight));
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
                context.DrawLine(new Pen(WaveBrush, 2), new Point(x, 0), new Point(x, waveHeight));
                context.FillRectangle(WaveBrush, new Rect(Math.Clamp(x - 4, 0, Math.Max(0, bounds.Width - 8)), 0, 8, 18), 3);
            }
            DrawRuler(context, bounds.Width, waveHeight);
            if (PlaybackPosition is double position && double.IsFinite(position))
            {
                double x = Math.Clamp(position / Duration * bounds.Width, 1, Math.Max(1, bounds.Width - 1));
                context.DrawLine(new Pen(PlaybackBrush, 2), new Point(x, 0), new Point(x, waveHeight));
                context.FillRectangle(PlaybackBrush, new Rect(Math.Clamp(x - 4, 0, Math.Max(0, bounds.Width - 8)), 0, 8, 8), 2);
            }
        }
    }

    private void DrawRuler(DrawingContext context, double width, double top)
    {
        FormattedText Label(double seconds) => new(seconds.ToString("0.######", CultureInfo.CurrentCulture) + " s",
            CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 12, RulerBrush);

        Pen tickPen = new(RulerBrush);
        FormattedText first = Label(0);
        FormattedText last = Label(Duration);
        double lastLeft = Math.Max(0, width - last.Width);
        context.DrawLine(tickPen, new Point(0, top), new Point(width, top));
        context.DrawText(first, new Point(0, top + 7));
        double previousRight = first.Width;
        double minimumStep = Duration / Math.Max(1, Math.Floor(width / 72));
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(minimumStep)));
        double normalized = minimumStep / magnitude;
        double step = (normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10) * magnitude;
        for (int index = 1; index * step < Duration; index++)
        {
            double seconds = index * step;
            double x = seconds / Duration * width;
            context.DrawLine(tickPen, new Point(x, top), new Point(x, top + 4));
            FormattedText label = Label(seconds);
            double left = x - label.Width / 2;
            if (left < previousRight + 8 || left + label.Width + 8 > lastLeft)
                continue;
            context.DrawText(label, new Point(left, top + 7));
            previousRight = left + label.Width;
        }
        if (lastLeft >= previousRight + 8)
            context.DrawText(last, new Point(lastLeft, top + 7));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Duration <= 0 || Bounds.Width <= 0 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        Focus();
        Point point = e.GetPosition(this);
        double x = point.X;
        double left = SelectionStart / Duration * Bounds.Width;
        double right = SelectionEnd / Duration * Bounds.Width;
        _handle = Math.Abs(x - left) <= 12 && Math.Abs(x - left) <= Math.Abs(x - right) ? -1
            : Math.Abs(x - right) <= 12 ? 1 : 0;
        if (PlaybackPosition is double cursor && Math.Abs(x - cursor / Duration * Bounds.Width) <= 8
            && (_handle == 0 || point.Y <= 8))
            _handle = 2;
        _anchor = ToSeconds(x);
        _pressX = x;
        _moved = false;
        _dragging = true;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging)
            return;
        double x = e.GetPosition(this).X;
        _moved |= Math.Abs(x - _pressX) > 4;
        if (!_moved)
            return;
        double position = ToSeconds(x);
        if (_handle == 2)
            Seek(position);
        else if (_handle == -1)
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
        if (!_dragging)
            return;
        if (!_moved)
            Seek(ToSeconds(e.GetPosition(this).X));
        _dragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragging = false;
    }

    private double ToSeconds(double x) => Math.Clamp(x / Bounds.Width, 0, 1) * Duration;

    private void Seek(double position)
    {
        double bounded = Math.Clamp(position, SelectionStart, SelectionEnd);
        if (SeekCommand?.CanExecute(bounded) == true)
            SeekCommand.Execute(bounded);
    }

    private void SetRange(double start, double end)
    {
        SetCurrentValue(SelectionStartProperty, 0d);
        SetCurrentValue(SelectionEndProperty, end);
        SetCurrentValue(SelectionStartProperty, start);
    }
}