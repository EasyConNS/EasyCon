using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System;

namespace EasyCon2.Avalonia.Core.TagEditor;

/// <summary>
/// 圈选模式
/// </summary>
public enum SelectionMode
{
    None,
    Range,
    Target
}

/// <summary>
/// 支持缩放、平移和圈选的图片控件
/// </summary>
public class SelectableImage : Control
{
    public static readonly StyledProperty<IImage?> SourceProperty =
        AvaloniaProperty.Register<SelectableImage, IImage?>(nameof(Source));

    public static readonly StyledProperty<SelectionMode> SelectionModeProperty =
        AvaloniaProperty.Register<SelectableImage, SelectionMode>(nameof(SelectionMode));

    public static readonly StyledProperty<Rect> RangeRectProperty =
        AvaloniaProperty.Register<SelectableImage, Rect>(nameof(RangeRect));

    public static readonly StyledProperty<Rect> TargetRectProperty =
        AvaloniaProperty.Register<SelectableImage, Rect>(nameof(TargetRect));

    public IImage? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public SelectionMode SelectionMode
    {
        get => GetValue(SelectionModeProperty);
        set => SetValue(SelectionModeProperty, value);
    }

    public Rect RangeRect
    {
        get => GetValue(RangeRectProperty);
        set => SetValue(RangeRectProperty, value);
    }

    public Rect TargetRect
    {
        get => GetValue(TargetRectProperty);
        set => SetValue(TargetRectProperty, value);
    }

    // 视图状态
    private double _zoom = 1.0;
    private Point _offset; // 图片左上角在控件中的位置
    private Size _imageSize;

    // 交互状态
    private bool _isPanning;
    private Point _panStart;
    private bool _isSelecting;
    private Point _selectStartImg; // 图片坐标
    private Point _selectCurrentImg;

    public SelectableImage()
    {
        ClipToBounds = true;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    static SelectableImage()
    {
        SourceProperty.Changed.AddClassHandler<SelectableImage>((x, _) => x.OnSourceChanged());
        RangeRectProperty.Changed.AddClassHandler<SelectableImage>((x, _) => x.InvalidateVisual());
        TargetRectProperty.Changed.AddClassHandler<SelectableImage>((x, _) => x.InvalidateVisual());
    }

    private void OnSourceChanged()
    {
        if (Source != null)
        {
            _imageSize = Source.Size;
            FitToView();
        }
        InvalidateVisual();
    }

    /// <summary>
    /// 图片适应控件大小
    /// </summary>
    public void FitToView()
    {
        if (Source == null || Bounds.Width <= 0 || Bounds.Height <= 0) return;

        var scaleX = Bounds.Width / _imageSize.Width;
        var scaleY = Bounds.Height / _imageSize.Height;
        _zoom = Math.Min(scaleX, scaleY) * 0.9;

        // 居中
        _offset = new Point(
            (Bounds.Width - _imageSize.Width * _zoom) / 2,
            (Bounds.Height - _imageSize.Height * _zoom) / 2
        );
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Source == null) return;

        var mouse = e.GetPosition(this);
        var oldZoom = _zoom;
        var factor = e.Delta.Y > 0 ? 1.1 : 1 / 1.1;
        _zoom = Math.Clamp(_zoom * factor, 0.05, 50);

        // 以鼠标位置为中心缩放
        var ratio = _zoom / oldZoom;
        _offset = new Point(
            mouse.X - (mouse.X - _offset.X) * ratio,
            mouse.Y - (mouse.Y - _offset.Y) * ratio
        );

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Source == null) return;

        var props = e.GetCurrentPoint(this).Properties;
        var screen = e.GetPosition(this);

        if (SelectionMode != SelectionMode.None && props.IsRightButtonPressed)
        {
            _isSelecting = true;
            _selectStartImg = ScreenToImage(screen);
            _selectCurrentImg = _selectStartImg;
            e.Handled = true;
        }
        else if (props.IsMiddleButtonPressed ||
                 (SelectionMode == SelectionMode.None && props.IsLeftButtonPressed))
        {
            _isPanning = true;
            _panStart = screen;
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Source == null) return;

        var screen = e.GetPosition(this);

        if (_isSelecting)
        {
            _selectCurrentImg = ScreenToImage(screen);
            InvalidateVisual();
            e.Handled = true;
        }
        else if (_isPanning)
        {
            _offset += screen - _panStart;
            _panStart = screen;
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_isSelecting)
        {
            _isSelecting = false;
            var rect = MakeRect(_selectStartImg, _selectCurrentImg);

            if (SelectionMode == SelectionMode.Range)
                RangeRect = rect;
            else
                TargetRect = rect;

            InvalidateVisual();
            e.Handled = true;
        }

        _isPanning = false;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _isPanning = false;
        _isSelecting = false;
    }

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);
        if (Source == null) return;

        // 背景
        ctx.DrawRectangle(Brushes.Transparent, null, Bounds);

        // 图片变换：先缩放，再平移
        var transform = Matrix.CreateScale(_zoom, _zoom) * Matrix.CreateTranslation(_offset);
        using (ctx.PushTransform(transform))
        {
            // 绘制图片
            Source.Draw(ctx, new Rect(_imageSize), new Rect(_imageSize));

            // 范围矩形（红色）
            if (RangeRect.Width > 0 && RangeRect.Height > 0)
                ctx.DrawRectangle(null, new Pen(Brushes.Red, 1.5 / _zoom), RangeRect);

            // 目标矩形（绿色）
            if (TargetRect.Width > 0 && TargetRect.Height > 0)
                ctx.DrawRectangle(null, new Pen(Brushes.LimeGreen, 1.5 / _zoom), TargetRect);

            // 当前圈选
            if (_isSelecting)
            {
                var rect = MakeRect(_selectStartImg, _selectCurrentImg);
                var brush = SelectionMode == SelectionMode.Range ? Brushes.Red : Brushes.LimeGreen;
                ctx.DrawRectangle(null, new Pen(brush, 1.5 / _zoom, dashStyle: DashStyle.Dash), rect);
            }
        }
    }

    /// <summary>
    /// 屏幕坐标 → 图片坐标
    /// </summary>
    private Point ScreenToImage(Point screen)
    {
        // 逆变换：先减去平移，再除以缩放
        return new Point(
            (screen.X - _offset.X) / _zoom,
            (screen.Y - _offset.Y) / _zoom
        );
    }

    private static Rect MakeRect(Point a, Point b)
    {
        return new Rect(
            Math.Min(a.X, b.X),
            Math.Min(a.Y, b.Y),
            Math.Abs(b.X - a.X),
            Math.Abs(b.Y - a.Y));
    }
}
