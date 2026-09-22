using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Collections.Generic;

namespace EasyCon2.Avalonia.Controls;

/// <summary>
/// 由 <c>Resources/Icons.json</c> 数据驱动的矢量图标控件。
/// 图形按 64×64 设计画布绘制，并等比缩放到 <see cref="Visual.Bounds"/>。
/// </summary>
public sealed class IconView : Control
{
    private const double DesignSize = 64.0;

    private static readonly Dictionary<string, Geometry> GeometryCache = new(StringComparer.Ordinal);

    public static readonly StyledProperty<string> IconIdProperty =
        AvaloniaProperty.Register<IconView, string>(nameof(IconId), "");

    public static readonly StyledProperty<IBrush?> BrushProperty =
        AvaloniaProperty.Register<IconView, IBrush?>(nameof(Brush));

    static IconView()
    {
        AffectsRender<IconView>(IconIdProperty, BrushProperty);
    }

    /// <summary>图标 id，对应 Icons.json 的键（如 ico-unbound / key-4）。</summary>
    public string IconId
    {
        get => GetValue(IconIdProperty);
        set => SetValue(IconIdProperty, value);
    }

    /// <summary>图标着色画刷。</summary>
    public IBrush? Brush
    {
        get => GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        IReadOnlyList<IconShape>? shapes = IconRegistry.Get(IconId);
        IBrush? brush = Brush;
        if (shapes == null || brush == null || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        double scaleX = Bounds.Width / DesignSize;
        double scaleY = Bounds.Height / DesignSize;

        using (context.PushTransform(Matrix.CreateScale(scaleX, scaleY)))
        {
            foreach (IconShape shape in shapes)
            {
                Geometry geometry = GetGeometry(shape.Data);

                if (shape.Fill)
                {
                    using (context.PushOpacity(shape.Opacity))
                    {
                        context.DrawGeometry(brush, null, geometry);
                    }
                }

                if (shape.Stroke)
                {
                    context.DrawGeometry(null, new Pen(brush, shape.Width), geometry);
                }
            }
        }
    }

    private static Geometry GetGeometry(string data)
    {
        lock (GeometryCache)
        {
            if (GeometryCache.TryGetValue(data, out Geometry? cached))
                return cached;

            Geometry geometry = Geometry.Parse(data);
            GeometryCache[data] = geometry;
            return geometry;
        }
    }
}