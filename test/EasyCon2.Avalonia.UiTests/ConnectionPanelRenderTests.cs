using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Core.Config;
using EasyCon2.Avalonia.Localization;
using EasyCon2.Avalonia.Markup;
using EasyCon2.Avalonia.Services;
using EasyCon2.Avalonia.Shell;
using SkiaSharp;
using System.Text.Json;

namespace EasyCon2.Avalonia.UiTests;

public partial class MainWindowSaveTests
{
    [TestCase("classic", 1200)]
    [TestCase("classic", 800)]
    [TestCase("card", 1200)]
    [TestCase("card", 800)]
    [Explicit("M18 真实连接区渲染、悬停和键盘选择；使用测试连接服务，不代表实机验收")]
    [Category("Manual")]
    public void ConnectionPanels_RenderShortStatesAndStableWidths_AndExposeFullEndpointNames(string layout, int width)
    {
        string output = Environment.GetEnvironmentVariable("EASYCON_CONNECTION_RENDER_DIR")
            ?? Path.Combine(Path.GetTempPath(), "opencode", "connection-qa");
        Directory.CreateDirectory(output);
        ResourceDictionary locale = (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri("avares://EasyCon2.Avalonia/Resources/Locales/zh_CN.axaml"));
        Application.Current!.Resources.MergedDictionaries.Add(locale);
        Func<string, string>? previousResolver = L10nBridge.Resolver;
        L10nBridge.Resolver = L10n.T;
        TestServices services = new()
        {
            Ports = ["COM3", "通信端口-" + new string('P', 110) + " [COM4]", "通信端口-" + new string('P', 110) + " [COM5]"],
            VideoSources = ["USB Video", "同名采集器-" + new string('V', 110) + " [DSHOW #0]", "同名采集器-" + new string('V', 110) + " [DSHOW #1]"],
        };
        MainWindowViewModel vm = CreateViewModel(services, new ConfigState { ThemeStyleName = layout }, new AvaloniaUiDispatcher());
        MainWindow window = new(initializeEditorServices: false) { DataContext = vm, Width = width, Height = 800 };
        List<object> evidence = [];
        try
        {
            window.Show();
            window.Activate();
            string prefix = layout == "classic" ? "Classic" : "Card";
            ComboBox input = window.FindControl<ComboBox>(prefix + "ControllerSource")!;
            ComboBox capture = window.FindControl<ComboBox>(prefix + "CaptureSource")!;
            WaitUntil(() => input.IsEffectivelyVisible && input.Bounds.Width > 0, TimeSpan.FromSeconds(5), "连接区域没有显示");
            window.CaptureRenderedFrame()?.Dispose();
            double inputWidth = input.Bounds.Width, captureWidth = capture.Bounds.Width;
            for (int nameIndex = 0; nameIndex < 3; nameIndex++)
            {
                input.SelectedIndex = nameIndex;
                capture.SelectedIndex = nameIndex;
                foreach ((bool inputConnected, bool captureConnected) in new[] { (false, false), (true, false), (true, true), (false, true) })
                {
                    SetConnected(vm, services, inputConnected, captureConnected);
                    using WriteableBitmap frame = window.CaptureRenderedFrame()!;
                    Assert.Multiple(() =>
                    {
                        Assert.That(input.Bounds.Width, Is.EqualTo(inputWidth).Within(0.01));
                        Assert.That(capture.Bounds.Width, Is.EqualTo(captureWidth).Within(0.01));
                        Assert.That(vm.Switch.NintendoSwitchStatus, Is.EqualTo(L10n.T(inputConnected ? "Text.Status.Connected" : "Text.Status.NotConnected")));
                        Assert.That(vm.Capture.CaptureSourceStatus, Is.EqualTo(L10n.T(captureConnected ? "Text.Status.Connected" : "Text.Status.NotConnected")));
                        Assert.That(ToolTip.GetTip(input), Is.EqualTo(services.Ports[nameIndex]));
                        Assert.That(ToolTip.GetTip(capture), Is.EqualTo(services.VideoSources[nameIndex]));
                    });
                    foreach (ComboBox combo in new[] { input, capture })
                    {
                        TextBlock selected = combo.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == (string)combo.SelectedItem!);
                        Assert.That(selected.TextTrimming, Is.EqualTo(TextTrimming.CharacterEllipsis));
                        Assert.That(selected.Bounds.Width, Is.LessThan(combo.Bounds.Width - 12), "选中文本必须给下拉箭头留出空间");
                    }
                    foreach (Button button in window.GetVisualDescendants().OfType<Button>()
                        .Where(button => button.IsEffectivelyVisible && button.Classes.Contains("connection-action")))
                    {
                        TextBlock text = button.GetVisualDescendants().OfType<TextBlock>().Single();
                        Point end = text.TranslatePoint(new Point(text.Bounds.Width, text.Bounds.Height), button)!.Value;
                        Assert.That(end.X, Is.LessThanOrEqualTo(button.Bounds.Width + 0.1), $"按钮文字超出边界: {text.Text}");
                    }
                    string image = $"{layout}-{width}-name{nameIndex}-input{inputConnected}-capture{captureConnected}.png";
                    SaveConnectionFrame(frame, Path.Combine(output, image));
                    evidence.Add(new { layout, width, nameIndex, inputConnected, captureConnected, inputWidth, captureWidth, image });
                }
            }
            capture.SelectedIndex = 1;
            capture.Focus();
            capture.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            Assert.That(capture.ContainerFromIndex(1)?.DataContext, Is.EqualTo(services.VideoSources[1]));
            Assert.That(capture.ContainerFromIndex(2)?.DataContext, Is.EqualTo(services.VideoSources[2]));
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            WaitUntil(() => !capture.IsDropDownOpen && capture.SelectedIndex == 2, TimeSpan.FromSeconds(3), "未选中第二个同名来源");
            Assert.That(vm.Capture.SelectedCaptureSource, Is.EqualTo(services.VideoSources[2]));
            TextBlock tooltipTarget = capture.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == services.VideoSources[2]);
            ToolTip.SetShowDelay(tooltipTarget, 0);
            window.MouseMove(new Point(10, 100));
            Point hover = tooltipTarget.TranslatePoint(new Point(tooltipTarget.Bounds.Width / 2, tooltipTarget.Bounds.Height / 2), window)!.Value;
            window.MouseMove(hover);
            WaitUntil(() => ToolTip.GetIsOpen(tooltipTarget), TimeSpan.FromSeconds(3), "实际悬停没有显示完整来源 tooltip");
            Assert.That(ToolTip.GetTip(tooltipTarget), Is.EqualTo(services.VideoSources[2]));
            using WriteableBitmap hoverFrame = window.CaptureRenderedFrame()!;
            SaveConnectionFrame(hoverFrame, Path.Combine(output, $"{layout}-{width}-hover.png"));
            evidence.Add(new { layout, width, hover = "passed", duplicateSelection = "passed", fullName = services.VideoSources[2] });
            File.WriteAllText(Path.Combine(output, $"{layout}-{width}.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            ToolTip.SetIsOpen(window.FindControl<ComboBox>((layout == "classic" ? "Classic" : "Card") + "CaptureSource")!, false);
            window.Close();
            vm.OnMainWindowClosing();
            L10nBridge.Resolver = previousResolver;
            Application.Current.Resources.MergedDictionaries.Remove(locale);
        }
    }

    private static void SetConnected(MainWindowViewModel vm, TestServices services, bool input, bool capture)
    {
        if (services.InputConnected != input)
        {
            Task command = ((IAsyncRelayCommand)vm.Switch.ConnectNintendoSwitchCommand).ExecuteAsync(null);
            WaitUntil(() => command.IsCompleted && !vm.Switch.IsConnectingNintendoSwitch && vm.Switch.IsNintendoSwitchConnected == input,
                TimeSpan.FromSeconds(5), "手柄连接命令未完成");
            command.GetAwaiter().GetResult();
        }
        if (services.VideoConnected != capture)
        {
            Task command = ((IAsyncRelayCommand)vm.Capture.ConnectCaptureSourceCommand).ExecuteAsync(null);
            WaitUntil(() => command.IsCompleted && !vm.Capture.IsConnectingCaptureSource && vm.Capture.IsCaptureSourceConnected == capture,
                TimeSpan.FromSeconds(5), "采集连接命令未完成");
            command.GetAwaiter().GetResult();
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static void SaveConnectionFrame(WriteableBitmap frame, string path)
    {
        using ILockedFramebuffer buffer = frame.Lock();
        SKImageInfo info = new(buffer.Size.Width, buffer.Size.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using SKImage image = SKImage.FromPixels(info, buffer.Address, buffer.RowBytes);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Create(path);
        data.SaveTo(stream);
    }

    private sealed partial class TestServices
    {
        public string[] Ports { get; init; } = [];
        public string[] VideoSources { get; init; } = [];
        public bool InputConnected { get; private set; }
        public bool VideoConnected { get; private set; }
        bool IDeviceService.IsConnected => InputConnected;
        bool ICaptureService.IsConnected => VideoConnected;
        bool IDeviceService.TryConnect(string port)
        {
            InputConnected = Ports.Contains(port);
            ConnectionStateChanged?.Invoke();
            return InputConnected;
        }
        bool ICaptureService.TryConnect(string sourceName)
        {
            VideoConnected = VideoSources.Contains(sourceName);
            ConnectionStateChanged?.Invoke();
            return VideoConnected;
        }
        void IDeviceService.Disconnect()
        {
            InputConnected = false;
            ConnectionStateChanged?.Invoke();
        }
        Task ICaptureService.DisconnectAsync()
        {
            VideoConnected = false;
            ConnectionStateChanged?.Invoke();
            return Task.CompletedTask;
        }
    }
}