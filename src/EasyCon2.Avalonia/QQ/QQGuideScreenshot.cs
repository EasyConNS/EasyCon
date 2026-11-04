using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;

namespace EasyCon2.Avalonia.QQ;

/// <summary>按步骤裁剪原图，并在同一坐标系中绘制点击提示。</summary>
public sealed class QQGuideScreenshot : Control
{
    public static readonly StyledProperty<QQGuideStep?> StepProperty =
        AvaloniaProperty.Register<QQGuideScreenshot, QQGuideStep?>(nameof(Step));

    public static readonly StyledProperty<bool> FullImageProperty =
        AvaloniaProperty.Register<QQGuideScreenshot, bool>(nameof(FullImage));

    private static readonly Pen _focusPen = new(new SolidColorBrush(Color.Parse("#EE8050")), 3);
    private Bitmap? _bitmap;
    private bool _attached;

    static QQGuideScreenshot()
    {
        AffectsMeasure<QQGuideScreenshot>(StepProperty, FullImageProperty);
        AffectsRender<QQGuideScreenshot>(StepProperty, FullImageProperty);
        StepProperty.Changed.AddClassHandler<QQGuideScreenshot>((control, _) => control.LoadImage());
    }

    public QQGuideStep? Step
    {
        get => GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public bool FullImage
    {
        get => GetValue(FullImageProperty);
        set => SetValue(FullImageProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        LoadImage();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        _bitmap?.Dispose();
        _bitmap = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void LoadImage()
    {
        _bitmap?.Dispose();
        _bitmap = null;
        if (_attached && Step is { } step)
        {
            using Stream image = AssetLoader.Open(step.ImageUri);
            _bitmap = new Bitmap(image);
        }
        InvalidateVisual();
    }

    private Rect SourceRect => Step is { } step
        ? FullImage ? new Rect(0, 0, step.PixelWidth, step.PixelHeight) : step.Crop
        : default;

    protected override Size MeasureOverride(Size availableSize)
    {
        Rect source = SourceRect;
        if (source.Width <= 0 || source.Height <= 0)
            return default;
        double scale = Math.Min(1, Math.Min(availableSize.Width / source.Width, availableSize.Height / source.Height));
        return new Size(source.Width * scale, source.Height * scale);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_bitmap == null || Step is not { } step || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;
        Rect source = SourceRect;
        double scale = Math.Min(Bounds.Width / source.Width, Bounds.Height / source.Height);
        double width = source.Width * scale;
        double height = source.Height * scale;
        Rect destination = new((Bounds.Width - width) / 2, (Bounds.Height - height) / 2, width, height);
        double pixelScaleX = _bitmap.Size.Width / step.PixelWidth;
        double pixelScaleY = _bitmap.Size.Height / step.PixelHeight;
        Rect bitmapSource = new(source.X * pixelScaleX, source.Y * pixelScaleY,
            source.Width * pixelScaleX, source.Height * pixelScaleY);
        context.DrawImage(_bitmap, bitmapSource, destination);
        using (context.PushClip(destination))
        {
            foreach (Rect focus in step.Focus)
            {
                Rect marker = new(
                    destination.X + ((focus.X - source.X) * scale),
                    destination.Y + ((focus.Y - source.Y) * scale),
                    focus.Width * scale, focus.Height * scale);
                context.DrawRectangle(null, _focusPen, marker, 4, 4);
            }
        }
    }
}