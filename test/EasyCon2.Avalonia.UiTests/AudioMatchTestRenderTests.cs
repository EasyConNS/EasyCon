using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using EasyCon.Capture.Audio;
using EasyCon2.Avalonia.AudioLabels;
using EasyCon2.Avalonia.Localization;
using EasyCon2.Avalonia.Markup;
using EasyCon2.Avalonia.Models;
using EasyCon2.Avalonia.Services;
using SkiaSharp;
using System.Buffers.Binary;
using System.Diagnostics;

namespace EasyCon2.Avalonia.UiTests;

[TestFixture]
[NonParallelizable]
public class AudioMatchTestRenderTests
{
    [OneTimeSetUp]
    public void Initialize()
    {
        if (Application.Current == null)
            TestAppBuilder.BuildAvaloniaApp().SetupWithoutStarting();
        Application.Current!.Styles.Add(new FluentTheme());
        Application.Current.Styles.Add(new StyleInclude(new Uri("avares://EasyCon2.Avalonia/"))
        {
            Source = new Uri("avares://EasyCon2.Avalonia/Resources/Styles/EasyConWorkbenchTheme.axaml")
        });
    }

    [TestCase("zh_CN", "声音匹配测试")]
    [TestCase("en_US", "Audio match test")]
    public void ThresholdBindingAndLoadedTemplateWorkAtMinimumSize(string locale, string title)
    {
        WithWindow(locale, ThemeManager.whiteGraySchemeName, (window, vm) =>
        {
            window.Width = window.MinWidth;
            window.Height = window.MinHeight;
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.That(frame, Is.Not.Null);
            Assert.That(window.Title, Is.EqualTo(title));
            Assert.That(window.FindControl<Button>("StartMatchButton")!.IsEnabled, Is.True);
            NumericUpDown input = window.FindControl<NumericUpDown>("ThresholdInput")!;
            Assert.That(input.Value, Is.EqualTo(85m));
            Assert.That(input.Minimum, Is.EqualTo(10m));
            Assert.That(input.Maximum, Is.EqualTo(100m));
            input.Value = 92m;
            Dispatcher.UIThread.RunJobs();
            Assert.That(vm.Threshold, Is.EqualTo(92));
            vm.Score = 95.1;
            Dispatcher.UIThread.RunJobs();
            Assert.That(window.FindControl<TextBlock>("CurrentScoreText")!.Text, Is.EqualTo(vm.ScoreText));
            Assert.That(window.FindControl<TextBlock>("TemplateSummary")!.Text, Does.Contain("0.500"));
            Assert.That(vm.Status, Does.Not.StartWith("Text."));
            Assert.That(input.Bounds.Width, Is.GreaterThan(250));
        });
    }

    [Test]
    [Explicit("Creates screenshots using the safe direct Skia encoder.")]
    [Category("Manual")]
    public void RenderAudioMatchPreviews()
    {
        foreach ((string locale, string scheme, string name) in new[]
        {
            ("zh_CN", ThemeManager.whiteGraySchemeName, "audio-match-light"),
            ("zh_CN", ThemeManager.DarkModeSchemeName, "audio-match-dark"),
            ("en_US", ThemeManager.whiteGraySchemeName, "audio-match-english")
        })
        {
            WithWindow(locale, scheme, (window, vm) =>
            {
                vm.Score = 92.7;
                vm.PeakScore = 96.2;
                vm.HitCount = 2;
                vm.Hits.Add(new AudioMatchHitRow("14:32:08.120", "91.40", "85.00"));
                vm.Hits.Add(new AudioMatchHitRow("14:31:56.480", "88.70", "85.00"));
                using WriteableBitmap frame = window.CaptureRenderedFrame()!;
                string output = Path.Combine(Path.GetTempPath(), "easycon-audio-match-qa");
                Directory.CreateDirectory(output);
                string path = Path.Combine(output, name + ".png");
                using ILockedFramebuffer buffer = frame.Lock();
                SKImageInfo info = new(buffer.Size.Width, buffer.Size.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                using SKImage image = SKImage.FromPixels(info, buffer.Address, buffer.RowBytes);
                using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
                using FileStream stream = File.Create(path);
                data.SaveTo(stream);
                TestContext.Out.WriteLine(path);
            });
        }
    }

    private static void WithWindow(string locale, string scheme, Action<AudioMatchTestWindow, AudioMatchTestViewModel> action)
    {
        ResourceDictionary dictionary = (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri($"avares://EasyCon2.Avalonia/Resources/Locales/{locale}.axaml"));
        Application.Current!.Resources.MergedDictionaries.Add(dictionary);
        Func<string, string>? resolver = L10nBridge.Resolver;
        L10nBridge.Resolver = L10n.T;
        ThemeManager.Instance.ApplyColorScheme(scheme);
        string directory = Path.Combine(Path.GetTempPath(), "easycon-audio-match-ui-" + Guid.NewGuid().ToString("N"));
        byte[] pcm = new byte[8000];
        for (int index = 0; index < pcm.Length / 2; index++)
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(index * 2), (short)(20000 * Math.Sin(index * Math.PI * 2 * 700 / 8000)));
        string label = AudioLabelStore.Save(directory, locale == "zh_CN" ? "闪光音效" : "Shiny sound", new AudioClip(new AudioFormat(8000, 1, 16, "pcm"), pcm));
        using AudioMatchTestViewModel vm = new(new FakeSource(), new FakeDialogs(label), SynchronousUiDispatcher.Instance);
        AudioMatchTestWindow window = new() { DataContext = vm };
        SynchronizationContext? previousContext = SynchronizationContext.Current;
        // NUnit restores its own context between fixtures. Install the real UI context for
        // async command continuations even when another fixture initialized Avalonia first.
        SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext(DispatcherPriority.Normal));
        try
        {
            window.Show();
            Task loading = vm.OpenCommand.ExecuteAsync(null);
            Stopwatch timeout = Stopwatch.StartNew();
            while (!loading.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(5))
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Yield();
            }
            Assert.That(loading.IsCompletedSuccessfully, Is.True);
            Dispatcher.UIThread.RunJobs();
            Assert.That(vm.HasTemplate, Is.True);
            action(window, vm);
        }
        finally
        {
            window.Close();
            Directory.Delete(directory, true);
            L10nBridge.Resolver = resolver;
            Application.Current.Resources.MergedDictionaries.Remove(dictionary);
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private sealed class FakeSource : IAudioCaptureSource
    {
        public bool IsSupported => true;
        public AudioCaptureState State { get; } = new(AudioCaptureStatus.Disconnected);
        public event Action<AudioChunk>? DataAvailable { add { } remove { } }
        public event Action<AudioCaptureState>? StateChanged { add { } remove { } }
        public event Action<AudioLevel>? LevelChanged { add { } remove { } }
        public IReadOnlyList<AudioDeviceInfo> GetAvailableDevices() => [];
        public bool TryConnect(string deviceId, CancellationToken cancellationToken = default) => false;
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task<bool> DisconnectIfConnectingAsync() => Task.FromResult(false);
        public void Dispose() { }
    }

    private sealed class FakeDialogs(string label) : IDialogService
    {
        public Task<IReadOnlyList<string>> OpenFilesAsync(string title, IReadOnlyList<FileDialogFilter>? filters = null, string? suggestedStartPath = null) => Task.FromResult<IReadOnlyList<string>>([label]);
        public Task<string?> OpenFolderAsync(string title, string? suggestedStartPath = null) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string title, string defaultExtension, IReadOnlyList<FileDialogFilter>? filters = null, string? suggestedFileName = null) => Task.FromResult<string?>(null);
        public Task<DialogColor?> PickColorAsync(DialogColor current) => Task.FromResult<DialogColor?>(null);
        public Task<string[]?> ShowScriptArgsDialogAsync() => Task.FromResult<string[]?>(null);
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(false);
    }
}