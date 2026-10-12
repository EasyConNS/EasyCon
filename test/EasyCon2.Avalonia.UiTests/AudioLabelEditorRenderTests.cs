using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
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
using System.Diagnostics;

namespace EasyCon2.Avalonia.UiTests;

[TestFixture]
[NonParallelizable]
public class AudioLabelEditorRenderTests
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

    [Test]
    public void WaveformDragAndNumericInputUpdateTheActualSelection()
    {
        WithEditor("zh_CN", ThemeManager.whiteGraySchemeName, (window, vm) =>
        {
            Assert.That(vm.SelectionEnd, Is.EqualTo(3), "Initial maximum bindings must not clamp a newly captured clip to zero.");
            AudioWaveform waveform = window.FindControl<AudioWaveform>("Waveform")!;
            Point start = waveform.TranslatePoint(new Point(waveform.Bounds.Width * 0.2, 80), window)!.Value;
            Point end = waveform.TranslatePoint(new Point(waveform.Bounds.Width * 0.6, 80), window)!.Value;
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end);
            window.MouseUp(end, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Multiple(() =>
            {
                Assert.That(vm.SelectionStart, Is.EqualTo(0.6).Within(0.02));
                Assert.That(vm.SelectionEnd, Is.EqualTo(1.8).Within(0.02));
            });
            window.FindControl<NumericUpDown>("SelectionStartInput")!.Value = 0.75m;
            Dispatcher.UIThread.RunJobs();
            Assert.That(vm.SelectionStart, Is.EqualTo(0.75).Within(0.001));
            Assert.That(waveform.SelectionStart, Is.EqualTo(0.75).Within(0.001));
        });
    }

    [TestCase("zh_CN", "声音标签")]
    [TestCase("en_US", "Audio labels")]
    public void EditorFitsMinimumWidthAndUsesLocaleResources(string locale, string title)
    {
        WithEditor(locale, ThemeManager.DarkModeSchemeName, (window, vm) =>
        {
            window.Width = window.MinWidth;
            window.Height = window.MinHeight;
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.That(frame, Is.Not.Null);
            Assert.That(window.Title, Is.EqualTo(title));
            AudioWaveform waveform = window.FindControl<AudioWaveform>("Waveform")!;
            Assert.That(waveform.Bounds.Width, Is.GreaterThan(550));
            Button capture = window.FindControl<Button>("CaptureRecentButton")!;
            Assert.That(capture.Content?.ToString(), Does.Not.StartWith("Text."));
            Assert.That(vm.ClipSummary, Does.Contain("16,000"));
            Assert.That(vm.Status, Does.Not.StartWith("Text."));
        });
    }

    [Test]
    [Explicit("Creates preview images for visual QA using the safe direct Skia encoder.")]
    [Category("Manual")]
    public void RenderAudioLabelEditorPreviews()
    {
        foreach ((string locale, string scheme, string name) in new[]
        {
            ("zh_CN", ThemeManager.whiteGraySchemeName, "audio-label-light"),
            ("zh_CN", ThemeManager.DarkModeSchemeName, "audio-label-dark"),
            ("en_US", ThemeManager.whiteGraySchemeName, "audio-label-english")
        })
        {
            WithEditor(locale, scheme, (window, vm) =>
            {
                window.Height = 720;
                vm.SelectionStart = 1.05;
                vm.SelectionEnd = 1.85;
                using WriteableBitmap frame = window.CaptureRenderedFrame()!;
                string output = Path.Combine(Path.GetTempPath(), "easycon-audio-label-qa");
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

    private static void WithEditor(string locale, string scheme, Action<AudioLabelEditorWindow, AudioLabelEditorViewModel> action)
    {
        ResourceDictionary dictionary = (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri($"avares://EasyCon2.Avalonia/Resources/Locales/{locale}.axaml"));
        Application.Current!.Resources.MergedDictionaries.Add(dictionary);
        Func<string, string>? resolver = L10nBridge.Resolver;
        L10nBridge.Resolver = L10n.T;
        ThemeManager.Instance.ApplyColorScheme(scheme);
        FakeSource source = new();
        using AudioHistoryBuffer history = new(source);
        using AudioLabelEditorViewModel vm = new(history, new FakePreview(), new FakeDialogs());
        AudioLabelEditorWindow window = new() { DataContext = vm };
        try
        {
            window.Show();
            short[] samples = new short[16000 * 3 * 2];
            for (int index = 0; index < samples.Length / 2; index++)
            {
                double time = index / 16000d;
                double envelope = time is >= 1.05 and <= 1.85 ? 0.65 * Math.Pow(Math.Sin((time - 1.05) / 0.8 * Math.PI), 2) : 0.05;
                samples[index * 2] = samples[index * 2 + 1] = (short)(Math.Sin(time * Math.PI * 2 * 880) * envelope * short.MaxValue);
            }
            byte[] pcm = new byte[samples.Length * 2];
            Buffer.BlockCopy(samples, 0, pcm, 0, pcm.Length);
            source.Emit(pcm);
            Task capture = vm.CaptureRecentCommand.ExecuteAsync(null);
            Stopwatch timeout = Stopwatch.StartNew();
            while (!capture.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(5))
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Yield();
            }
            Assert.That(capture.IsCompletedSuccessfully, Is.True);
            vm.LabelName = locale == "zh_CN" ? "闪光音效" : "Shiny sound";
            using WriteableBitmap? initial = window.CaptureRenderedFrame();
            action(window, vm);
        }
        finally
        {
            window.Close();
            L10nBridge.Resolver = resolver;
            Application.Current.Resources.MergedDictionaries.Remove(dictionary);
        }
    }

    private sealed class FakeSource : IAudioCaptureSource
    {
        public bool IsSupported => true;
        public AudioCaptureState State { get; } = new(AudioCaptureStatus.Connected, Format: new AudioFormat(16000, 2, 16, "pcm"));
        public event Action<AudioCaptureState>? StateChanged { add { } remove { } }
        public event Action<AudioChunk>? DataAvailable;
        public event Action<AudioLevel>? LevelChanged { add { } remove { } }
        public void Emit(byte[] pcm) => DataAvailable?.Invoke(new AudioChunk(0, State.Format!, pcm, pcm.Length));
        public IReadOnlyList<AudioDeviceInfo> GetAvailableDevices() => [];
        public bool TryConnect(string deviceId, CancellationToken cancellationToken = default) => false;
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task<bool> DisconnectIfConnectingAsync() => Task.FromResult(false);
        public void Dispose() { }
    }

    private sealed class FakePreview : IAudioPreview
    {
        public bool IsSupported => true;
        public Task PlayAsync(AudioClip clip, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeDialogs : IDialogService
    {
        public Task<string?> OpenFolderAsync(string title, string? suggestedStartPath = null) => Task.FromResult<string?>(null);
        public Task<IReadOnlyList<string>> OpenFilesAsync(string title, IReadOnlyList<FileDialogFilter>? filters = null, string? suggestedStartPath = null) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> SaveFileAsync(string title, string defaultExtension, IReadOnlyList<FileDialogFilter>? filters = null, string? suggestedFileName = null) => Task.FromResult<string?>(null);
        public Task<DialogColor?> PickColorAsync(DialogColor current) => Task.FromResult<DialogColor?>(null);
        public Task<string[]?> ShowScriptArgsDialogAsync() => Task.FromResult<string[]?>(null);
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(false);
    }
}