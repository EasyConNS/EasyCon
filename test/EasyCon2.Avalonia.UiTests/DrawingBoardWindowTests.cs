using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
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

    [TestCase("zh_CN", false, 0, false)]
    [TestCase("zh_CN", false, 0, true)]
    [TestCase("zh_CN", false, 1, false)]
    [TestCase("zh_CN", false, 2, false)]
    [TestCase("zh_CN", false, 3, false)]
    [TestCase("zh_CN", true, 3, false)]
    [TestCase("en_US", false, 0, true)]
    [TestCase("en_US", true, 3, false)]
    public void MinimumSize_NothingOverflows(string locale, bool eraserMode, int tab, bool coverMode)
    {
        // 用真实文案排版：未加载语言时控件显示的是键名，长短与实际不符
        ResourceDictionary dictionary = (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri($"avares://EasyCon2.Avalonia/Resources/Locales/{locale}.axaml"));
        Application.Current!.Resources.MergedDictionaries.Add(dictionary);
        // 参数页高度各不相同（等比裁切多出取景行，橡皮擦说明比普通提示长），每页都要放得下
        DrawingBoardWindow window = Show(480, 480, eraserMode, tab, coverMode);
        try
        {
            // 控件放不下时最小高度会随之抬高（桌面上窗口跟着长高，无头窗口不会），按抬高后的最小尺寸再排一次
            window.Height = window.MinHeight;
            _ = window.CaptureRenderedFrame();
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
            Application.Current.Resources.MergedDictionaries.Remove(dictionary);
        }
    }

    [Test]
    public void Preview_ScalesWithWindowInBothDirections()
    {
        double normal = PreviewWidthAt(900, 600);
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
        L10nBridge.Resolver = key => key == "Text.DrawingBoard.RegionRange" ? "{0},{1},{2},{3}" : null;
        DrawingBoardWindow window = Show(width, height);
        try
        {
            ((DrawingBoardViewModel)window.DataContext!).SelectRegion = true;
            Image picture = window.FindControl<Image>("PictureBox")!;
            Point center = picture.TranslatePoint(new Point(411, 141), window)!.Value;

            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);

            // 缩放后的点击位置会取整到像素，允许 ±1
            // 单击框出的区域就是这一个点：起止列、起止行
            int[] region = ((DrawingBoardViewModel)window.DataContext!).RegionText.Split(',').Select(int.Parse).ToArray();
            Assert.That(region[0], Is.EqualTo(160).Within(1));
            Assert.That(region[2], Is.EqualTo(60).Within(1));
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

    [Test]
    public void DraggingOnThePreview_SelectsTheRegion()
    {
        L10nBridge.Resolver = key => key == "Text.DrawingBoard.RegionRange" ? "{0}-{1},{2}-{3}" : null;
        DrawingBoardWindow window = Show(1400, 900);
        try
        {
            DrawingBoardViewModel vm = (DrawingBoardViewModel)window.DataContext!;
            vm.SelectRegion = true;
            Image picture = window.FindControl<Image>("PictureBox")!;
            // 逻辑坐标（822×282 坐标系）→ 窗口坐标；画布列 = x × 320 / 822
            Point Canvas(int col, int row) => picture.TranslatePoint(new Point((col + 0.5) * 822 / 320, (row + 0.5) * 282 / 120), window)!.Value;

            window.MouseDown(Canvas(10, 5), MouseButton.Left);
            window.MouseMove(Canvas(100, 40), RawInputModifiers.LeftMouseButton);
            window.MouseUp(Canvas(100, 40), MouseButton.Left);

            Assert.That(vm.RegionText, Is.EqualTo("10-100,5-40"));
        }
        finally
        {
            window.Close();
            L10nBridge.Resolver = null;
        }
    }

    [Test]
    public void EmptyPreview_ShowsTheDropHint()
    {
        DrawingBoardWindow window = Show(900, 600);
        try
        {
            DrawingBoardViewModel vm = (DrawingBoardViewModel)window.DataContext!;
            TextBlock hint = window.FindControl<TextBlock>("DropHint")!;
            Assert.That(hint.IsVisible, Is.False, "有图片时不显示提示");

            vm.Preview = null;
            Assert.That(hint.IsVisible, Is.True);
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void DroppingFiles_IsWiredToTheBoard()
    {
        DrawingBoardWindow window = Show(900, 560);
        try
        {
            Assert.That(EasyCon2.Avalonia.Behaviors.FileDropBehavior.GetCommand(window),
                Is.SameAs(((DrawingBoardViewModel)window.DataContext!).DropPictureCommand));
            Assert.That(DragDrop.GetAllowDrop(window), Is.True);
        }
        finally
        {
            window.Close();
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

    // 量外框：未载入原图时切换适配方式会清空预览，Image 排布为 0×0，外框仍按逻辑尺寸占位
    private static double PreviewWidth(DrawingBoardWindow window) => RenderedWidth(window.FindControl<Border>("PictureFrame")!, window);

    /// <summary>参数页与适配方式在显示前设好：显示后再切换，无头窗口会沿用先前撑大的尺寸排版，ClientSize 却不跟着变。</summary>
    private static DrawingBoardWindow Show(double width, double height, bool eraserMode = false, int tab = 0, bool coverMode = false)
    {
        DrawingBoardViewModel vm = new(new DisconnectedRunner(), new DialogService(), new SkiaImageProcessor(),
            SynchronousUiDispatcher.Instance, new LogService())
        {
            Preview = new PixelImage(320, 120, Enumerable.Repeat((byte)255, 320 * 120 * 4).ToArray()),
            EraserMode = eraserMode,
            FitModeIndex = coverMode ? 1 : 0,
        };
        DrawingBoardWindow window = new() { DataContext = vm, Width = width, Height = height };
        window.GetLogicalDescendants().OfType<TabControl>().Single().SelectedIndex = tab;
        window.Show();
        _ = window.CaptureRenderedFrame();
        return window;
    }

    private static double RenderedWidth(Control control, Visual root)
        => control.TranslatePoint(new Point(control.Bounds.Width, 0), root)!.Value.X - control.TranslatePoint(default, root)!.Value.X;

    private sealed class DisconnectedRunner : IDrawingRunner
    {
        public bool IsConnected => false;
        public void Run(Action<IPadInput, CancellationToken> draw, bool keepAwake, CancellationToken token) { }
    }
}