using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using EasyDevice;

namespace EasyCon2.Avalonia.Core.VPad;

internal class JCDrawControl : Control
{
    private readonly JCPainter _painter;
    private readonly DispatcherTimer _timer;

    public JCDrawControl(IReporter reporter, IControllerAdapter adapter)
    {
        _painter = new JCPainter(reporter, adapter, VPadResources.LoadImage);
        Width = 100;
        Height = 100;

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Render, (_, _) => InvalidateVisual());
        _timer.Start();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        _painter.OnPaint(context, new Rect(0, 0, 100, 100));
    }

    public void Stop()
    {
        _timer.Stop();
    }
}