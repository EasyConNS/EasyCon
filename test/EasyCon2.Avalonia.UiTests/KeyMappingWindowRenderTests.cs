using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using EasyCon2.Avalonia.Services;
using EasyCon2.Avalonia.ViewModels;
using EasyCon2.Avalonia.Views;
using SkiaSharp;
using System;
using System.IO;
using System.Linq;

namespace EasyCon2.Avalonia.UiTests;

/// <summary>
/// Headless Skia 渲染 / 回归测试：把按键映射窗口在三种配色方案下渲染成 PNG，
/// 便于人工比对线条颜色是否统一为固定的钢蓝色；并锁定画布尺寸等布局不变量。
///
/// ⚠ 两个写 PNG 的用例标记为 <c>[Explicit]</c> + <c>[Category("Manual")]</c>，默认不参与 CI：
/// 截图只是调试辅助，而 Avalonia 的 <c>WriteableBitmap.Save</c>（Avalonia.Skia.ImageSavingHelper
/// → SkiaSharp 原生 PNG 编码器）在本项目的 headless 环境下会**间歇性让测试主机原生崩溃**，症状是
/// 整轮随机少跑几个用例且退出码非零（<c>ci.yml</c> 的 build-and-test 会跑 <c>ci\test.bat</c>，
/// 因此会让 dev→main 的 PR 间歇性变红）。崩溃栈：
/// <code>
/// Fatal error. 测试主机进程崩溃
///   at SkiaSharp.SkiaApi.sk_pngencoder_encode(...)
///   at SkiaSharp.SKImage.Encode(...)
///   at Avalonia.Skia.Helpers.ImageSavingHelper.SaveImage(...)
///   at Avalonia.Skia.WriteableBitmapImpl.Save(...)
///   at KeyMappingWindowRenderTests.RendersPngForEveryColorScheme()
/// </code>
/// 规避见 <see cref="SaveFrame"/>：绕开 Avalonia 的保存路径，直接用 SkiaSharp 编码。
/// 根因未定位（managed/native 均为 SkiaSharp 4.148.0，与 Avalonia.Skia 12.0.4 一致，不是版本错配）。
/// 需人工看图时显式运行：
/// <code>dotnet test test\EasyCon2.Avalonia.UiTests -c Release --filter "TestCategory=Manual"</code>
/// </summary>
[TestFixture]
public class KeyMappingWindowRenderTests
{
    private static bool _stylesInitialized;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        if (_stylesInitialized)
            return;

        TestAppBuilder.BuildAvaloniaApp().SetupWithoutStarting();
        Application.Current!.Styles.Add(new FluentTheme());
        Application.Current.Styles.Add(new StyleInclude(new Uri("avares://EasyCon2.Avalonia/"))
        {
            Source = new Uri("avares://EasyCon2.Avalonia/Resources/Styles/EasyConWorkbenchTheme.axaml")
        });
        _stylesInitialized = true;
    }

    /// <summary>
    /// 把截帧写成 PNG。
    /// 刻意不用 <c>WriteableBitmap.Save</c>：它走 Avalonia.Skia 的 ImageSavingHelper →
    /// SkiaSharp 原生 PNG 编码器，在本项目的 headless 环境下会**间歇性让测试主机原生崩溃**
    /// （Fatal error at sk_pngencoder_encode），表现为整轮随机少跑几个用例且退出码非零。
    /// 这里把像素以零拷贝方式包成 SKImage 再用 SkiaSharp 编码，
    /// 与一直稳定的 SvgRenderSmokeTests 走同一条编码路径。
    /// </summary>
    private static void SaveFrame(WriteableBitmap frame, string path)
    {
        using ILockedFramebuffer buffer = frame.Lock();
        SKImageInfo info = new(buffer.Size.Width, buffer.Size.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using SKImage image = SKImage.FromPixels(info, buffer.Address, buffer.RowBytes);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Create(path);
        data.SaveTo(stream);
    }

    [Test]
    [Explicit("截图仅供人工比对，默认不在 CI 运行；见类注释（headless 下保存 PNG 会让测试主机原生崩溃）")]
    [Category("Manual")]
    public void RendersPngForEveryColorScheme()
    {
        Assert.That(ThemeManager.Instance, Is.Not.Null, "ThemeManager.Instance was not reachable");

        string[] schemes =
        {
            ThemeManager.whiteGraySchemeName,
            ThemeManager.WarmToneSchemeName,
            ThemeManager.DarkModeSchemeName
        };

        string outputDirectory = Path.Combine(Path.GetTempPath(), "opencode", "svgqa");
        Directory.CreateDirectory(outputDirectory);

        foreach (string scheme in schemes)
        {
            ThemeManager.Instance.ApplyColorScheme(scheme);

            KeyMappingWindow window = new() { DataContext = new KeyMappingViewModel() };
            window.Show();

            WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.That(frame, Is.Not.Null, "CaptureRenderedFrame returned null for scheme " + scheme);

            string path = Path.Combine(outputDirectory, $"mapping-{scheme}.png");
            SaveFrame(frame!, path);

            window.Close();

            Assert.That(File.Exists(path), Is.True, "screenshot not written: " + path);
            long length = new FileInfo(path).Length;
            Assert.That(length, Is.GreaterThan(5 * 1024), $"screenshot too small ({length} bytes): {path}");
            TestContext.Out.WriteLine($"scheme {scheme} -> {path} ({length} bytes)");
        }
    }

    /// <summary>
    /// 清空一个 ViewModel 的全部绑定，让冲突断言有确定的基线。
    /// 退格 = 清除绑定，因此逐行进入监听再按退格即可全部清空。
    /// </summary>
    private static void ClearAllBindings(KeyMappingViewModel vm)
    {
        foreach (KeyMappingRow row in vm.Rows)
        {
            vm.StartListeningCommand.Execute(row);
            vm.OnKeyDown(Key.Back);
        }
    }

    /// <summary>
    /// 按键冲突必须「接受编辑 + 红色标记」，且绑定完成后不得残留高亮；
    /// 退格清除绑定后冲突解除；系统保留键被拒绝且保持监听状态。
    /// </summary>
    [Test]
    public void ConflictIsMarkedButNeverBlocksEditing()
    {
        KeyMappingViewModel vm = new();
        ClearAllBindings(vm);

        KeyMappingRow first = vm.Rows[0];
        KeyMappingRow second = vm.Rows[3];

        // ① 首次绑定：写入成功、无冲突、监听与高亮都必须复位
        vm.StartListeningCommand.Execute(first);
        vm.OnKeyDown(Key.A);

        Assert.That(first.Scancode, Is.EqualTo(4), "Key.A must map to scancode 4");
        Assert.That(first.IsConflicted, Is.False, "a unique binding must not be flagged");
        Assert.That(first.IsHighlighted, Is.False, "highlight must clear once the binding is applied");
        Assert.That(vm.HighlightedControllerId, Is.Null, "controller highlight must clear once the binding is applied");

        // ② 重复绑定：仍要写入（不阻止编辑），且两行都标记为冲突
        vm.StartListeningCommand.Execute(second);
        vm.OnKeyDown(Key.A);

        Assert.That(second.Scancode, Is.EqualTo(4), "a conflicting binding must still be accepted");
        Assert.That(second.IsConflicted, Is.True, "the newly bound row must be flagged");
        Assert.That(first.IsConflicted, Is.True, "the pre-existing holder must be flagged too");
        Assert.That(second.IsHighlighted, Is.False, "highlight must clear even when conflicting");

        // ③ 退格清除重复绑定 → 冲突解除
        vm.StartListeningCommand.Execute(second);
        vm.OnKeyDown(Key.Back);

        Assert.That(second.Scancode, Is.EqualTo(0), "Backspace must clear the binding");
        Assert.That(first.IsConflicted, Is.False, "removing the duplicate must resolve the conflict");

        // ④ 系统保留键（徽标键）：拒绝写入、保持监听
        vm.StartListeningCommand.Execute(second);
        vm.OnKeyDown(Key.LWin);

        Assert.That(second.Scancode, Is.EqualTo(0), "a reserved key must never be written");
        Assert.That(vm.ListeningRow, Is.SameAs(second), "listening must stay active after a rejected key");
        Assert.That(vm.StatusText, Does.Contain("保留"), "status bar must explain the rejection");

        // ⑤ 清除键：ESC 同样能清除绑定
        vm.OnKeyDown(Key.Escape);
        vm.StartListeningCommand.Execute(first);
        vm.OnKeyDown(Key.Escape);

        Assert.That(first.Scancode, Is.EqualTo(0), "Escape must clear the binding");
    }

    /// <summary>把一组冲突绑定渲染成 PNG，便于人工确认红色的观感。</summary>
    [Test]
    [Explicit("截图仅供人工比对，默认不在 CI 运行；见类注释（headless 下保存 PNG 会让测试主机原生崩溃）")]
    [Category("Manual")]
    public void RendersConflictHighlight()
    {
        ThemeManager.Instance.ApplyColorScheme(ThemeManager.DarkModeSchemeName);

        KeyMappingViewModel vm = new();
        ClearAllBindings(vm);
        vm.StartListeningCommand.Execute(vm.Rows[0]);
        vm.OnKeyDown(Key.A);
        vm.StartListeningCommand.Execute(vm.Rows[3]);
        vm.OnKeyDown(Key.A);

        KeyMappingWindow window = new() { DataContext = vm };
        window.Show();

        WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.That(frame, Is.Not.Null, "CaptureRenderedFrame returned null");

        string directory = Path.Combine(Path.GetTempPath(), "opencode", "svgqa");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "mapping-conflict-dark.png");
        SaveFrame(frame!, path);

        window.Close();

        TestContext.Out.WriteLine($"conflict screenshot -> {path}");
        Assert.That(File.Exists(path), Is.True, "screenshot not written: " + path);
    }

    /// <summary>
    /// 状态栏文案不得改变画布尺寸 —— emoji（🎯 / ⚠）所属字体的行盒比正文高 2px，
    /// 若状态栏按内容自适应（38↔40），Grid 的 Auto 行会挤掉 * 行的高度，
    /// 使 Viewbox 重新等比缩放整个 1440×960 画布，表现为全屏小幅抖动。
    /// 本测试锁定 KeyMappingWindow.axaml 中状态栏 Border 的 MinHeight=40，请勿移除该属性。
    /// </summary>
    [Test]
    public void StatusBarTextMustNotResizeTheCanvas()
    {
        KeyMappingViewModel vm = new();
        KeyMappingWindow window = new() { DataContext = vm };
        window.Show();
        _ = window.CaptureRenderedFrame();

        string[] probes =
        [
            "",
            "plain",
            "🎯",
            "⚠ 按键衝突",
            "已将 L 绑定到「W」",
            "🎯 当前监听: L — 按下键盘按键 (Esc / 退格 清除绑定)",
        ];

        Size? expected = null;
        foreach (string probe in probes)
        {
            vm.StatusText = probe;
            _ = window.CaptureRenderedFrame();

            Viewbox? viewbox = window.GetVisualDescendants().OfType<Viewbox>().FirstOrDefault();
            Assert.That(viewbox, Is.Not.Null, "Viewbox not found in the window");

            if (expected == null)
            {
                expected = viewbox!.Bounds.Size;
                continue;
            }

            Assert.That(
                viewbox!.Bounds.Size,
                Is.EqualTo(expected.Value),
                $"状态栏文案改变了画布尺寸（说明 MinHeight 被移除）: \"{probe}\"");
        }

        window.Close();
    }
}