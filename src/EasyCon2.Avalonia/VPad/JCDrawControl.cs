using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using EasyDevice;

namespace EasyCon2.Avalonia.VPad;

internal class JCDrawControl : Control
{
    private readonly JCPainter _painter;
    private readonly IReporter _reporter;
    private readonly DispatcherTimer _timer;
    private byte[]? _lastReportBytes;

    public JCDrawControl(IReporter reporter, IControllerAdapter adapter)
    {
        _reporter = reporter;
        _painter = new JCPainter(reporter, adapter, VPadResources.LoadImage);
        Width = 100;
        Height = 100;

        // 状态变化才重绘：无输入时不再以 Render 优先级 10Hz 全量重绘空转
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Render, (_, _) =>
        {
            var bytes = _reporter.GetReport().GetBytes();
            if (_lastReportBytes == null || !bytes.AsSpan().SequenceEqual(_lastReportBytes))
            {
                _lastReportBytes = bytes;
                InvalidateVisual();
            }
        });
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