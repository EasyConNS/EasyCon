using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using EasyCon.Core.Capabilities;
using EasyCon2.Avalonia.DrawingBoard;
using EasyCon2.Avalonia.Localization;
using EasyCon2.Avalonia.Models;
using EasyCon2.Avalonia.Services;

namespace EasyCon2.Avalonia.UiTests;

/// <summary>画板窗口：预览随窗口等比缩放、最小尺寸下不溢出，对话框归属画板窗口。</summary>
[TestFixture]
public class DrawingBoardWindowTests
{
    private static bool _stylesInitialized;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        if (Application.Current == null)
            TestAppBuilder.BuildAvaloniaApp().SetupWithoutStarting();
        if (_stylesInitialized)
            return;
        Application.Current!.Styles.Add(new FluentTheme());
        Application.Current.Styles.Add(new StyleInclude(new Uri("avares://EasyCon2.Avalonia/"))
        {
            Source = new Uri("avares://EasyCon2.Avalonia/Resources/Styles/EasyConWorkbenchTheme.axaml")
        });
        _stylesInitialized = true;
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MinimumSize_NothingOverflows(bool eraserMode)
    {
        DrawingBoardWindow window = Show(480, 480, eraserMode);
        try
        {
            Assert.That(window.ClientSize, Is.EqualTo(new Size(window.MinWidth, window.MinHeight)));
            Size client = window.ClientSize;
            string[] overflowing = window.GetLogicalDescendants().OfType<Control>()
                .Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0)
                .Where(c => c.TranslatePoint(new Point(c.Bounds.Width, c.Bounds.Height), window) is { } corner
                    && (corner.X > client.Width + 0.5 || corner.Y > client.Height + 0.5))
                .Select(c => c.GetType().Name + (c.Name is { } name ? "#" + name : ""))
                .ToArray();

            Assert.That(overflowing, Is.Empty);
            Assert.That(PreviewWidth(window), Is.GreaterThan(150), "最小尺寸下预览仍应可用");
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void Preview_ScalesWithWindowInBothDirections()
    {
        double normal = PreviewWidthAt(900, 560);
        double larger = PreviewWidthAt(1400, 900);
        double wideButShort = PreviewWidthAt(1400, 480);

        Assert.That(normal, Is.GreaterThan(800), "默认尺寸下预览应接近填满宽度");
        Assert.That(larger, Is.GreaterThan(normal * 1.3), "窗口放大时预览应跟着放大");
        Assert.That(wideButShort, Is.LessThan(larger * 0.8), "同样宽度下窗口变矮，预览应受高度限制等比缩小");
    }

    [TestCase(480, 480)]
    [TestCase(1400, 900)]
    public void ScaledPreview_ClickMapsToCanvasCoordinates(double width, double height)
    {
        L10nBridge.Resolver = key => key == "Text.DrawingBoard.CurPos" ? "{0},{1}" : null;
        DrawingBoardWindow window = Show(width, height);
        try
        {
            Image picture = window.FindControl<Image>("PictureBox")!;
            Point center = picture.TranslatePoint(new Point(411, 141), window)!.Value;

            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);

            // 缩放后的点击位置会取整到像素，允许 ±1
            int[] pos = ((DrawingBoardViewModel)window.DataContext!).CurPosText.Split(',').Select(int.Parse).ToArray();
            Assert.That(pos[0], Is.EqualTo(160).Within(1));
            Assert.That(pos[1], Is.EqualTo(60).Within(1));
        }
        finally
        {
            window.Close();
            L10nBridge.Resolver = null;
        }
    }

    [Test]
    public void Messages_AreOwnedByTheDrawingBoardWindow()
    {
        // 全程同步、停留在 UI 线程，并换上 Avalonia 的同步上下文：否则提示框关闭后命令的续体会经 NUnit 的
        // 同步上下文跑到线程池上，AsyncRelayCommand 在那里刷新按钮状态，间歇性触发跨线程异常
        SynchronizationContext? previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext());
        DrawingBoardWindow window = DrawingBoardWindow.Create(new DisconnectedRunner(), new SkiaImageProcessor(),
            SynchronousUiDispatcher.Instance, new LogService());
        window.Show();
        try
        {
            Task start = ((DrawingBoardViewModel)window.DataContext!).StartCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();

            Assert.That(window.OwnedWindows, Has.Count.EqualTo(1), "未连接提示应以画板窗口为 Owner，不能被画板窗口挡住");
            window.OwnedWindows[0].Close();
            Dispatcher.UIThread.RunJobs();
            Assert.That(start.IsCompleted, Is.True);
        }
        finally
        {
            window.Close();
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private static double PreviewWidthAt(double width, double height)
    {
        DrawingBoardWindow window = Show(width, height);
        try
        {
            return PreviewWidth(window);
        }
        finally
        {
            window.Close();
        }
    }

    private static double PreviewWidth(DrawingBoardWindow window) => RenderedWidth(window.FindControl<Image>("PictureBox")!, window);

    private static DrawingBoardWindow Show(double width, double height, bool eraserMode = false)
    {
        DrawingBoardViewModel vm = new(new DisconnectedRunner(), new DialogService(), new SkiaImageProcessor(),
            SynchronousUiDispatcher.Instance, new LogService())
        {
            Preview = new PixelImage(320, 120, Enumerable.Repeat((byte)255, 320 * 120 * 4).ToArray()),
            EraserMode = eraserMode,
        };
        DrawingBoardWindow window = new() { DataContext = vm, Width = width, Height = height };
        window.Show();
        _ = window.CaptureRenderedFrame();
        return window;
    }

    private static double RenderedWidth(Control control, Visual root)
        => control.TranslatePoint(new Point(control.Bounds.Width, 0), root)!.Value.X - control.TranslatePoint(default, root)!.Value.X;

    private sealed class DisconnectedRunner : IDrawingRunner
    {
        public bool IsConnected => false;
        public void Run(Action<IPadInput, CancellationToken> draw, CancellationToken token) { }
    }
}