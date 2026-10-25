using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using EasyCon.Core.Capabilities;
using EasyCon2.Avalonia.DrawingBoard;
using EasyCon2.Avalonia.Localization;
using EasyCon2.Avalonia.Models;
using EasyCon2.Avalonia.Services;

namespace EasyCon2.Avalonia.UiTests;

/// <summary>画板窗口在窄屏下不溢出：预览等比缩小、参数分组换行。</summary>
[TestFixture]
public class DrawingBoardWindowLayoutTests
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

    [Test]
    public void NarrowWindow_NothingOverflowsHorizontally()
    {
        DrawingBoardWindow window = Show(520);
        try
        {
            double clientWidth = window.ClientSize.Width;
            string[] overflowing = window.GetLogicalDescendants().OfType<Control>()
                .Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0)
                .Where(c => c.TranslatePoint(new Point(c.Bounds.Width, 0), window) is { } right && right.X > clientWidth + 0.5)
                .Select(c => c.GetType().Name + (c.Name is { } name ? "#" + name : ""))
                .ToArray();

            Assert.That(overflowing, Is.Empty);
            Assert.That(RenderedWidth(window.FindControl<Image>("PictureBox")!, window), Is.GreaterThan(400).And.LessThan(822));
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void DefaultWidth_ShowsPreviewAtFullSize()
    {
        DrawingBoardWindow window = Show(900);
        try
        {
            Assert.That(RenderedWidth(window.FindControl<Image>("PictureBox")!, window), Is.EqualTo(822).Within(0.5));
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void ScaledPreview_ClickMapsToCanvasCoordinates()
    {
        L10nBridge.Resolver = key => key == "Text.DrawingBoard.CurPos" ? "{0},{1}" : null;
        DrawingBoardWindow window = Show(520);
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

    private static DrawingBoardWindow Show(double width)
    {
        DrawingBoardViewModel vm = new(new DisconnectedRunner(), new DialogService(), new SkiaImageProcessor(),
            SynchronousUiDispatcher.Instance, new LogService())
        {
            Picture = new PixelImage(320, 120, Enumerable.Repeat((byte)255, 320 * 120 * 4).ToArray()),
        };
        DrawingBoardWindow window = new() { DataContext = vm, Width = width };
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