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
using Avalonia.VisualTree;
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
            Assert.That(vm.PlaybackSummary, Does.Not.StartWith("Text."));
            Assert.That(window.FindControl<TextBlock>("SaveLocationHint")!.Text, Does.Contain("audiolabel").And.Not.StartWith("Text."));
        });
    }

    [TestCase("zh_CN", "当前位置")]
    [TestCase("en_US", "Position")]
    public void PreviewUpdatesCursorAndLocalizedReadoutOnTheUiThreadAndStopKeepsBoth(string locale, string caption)
    {
        FakePreview preview = new() { WaitForCancellation = true };
        WithEditor(locale, ThemeManager.DarkModeSchemeName, (window, vm) =>
        {
            vm.SelectionStart = 1.05;
            vm.SelectionEnd = 1.85;
            Task playing = vm.PlaySelectionCommand.ExecuteAsync(null);
            AudioWaveform waveform = window.FindControl<AudioWaveform>("Waveform")!;
            bool updatedOnUiThread = false;
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(vm.PlaybackPosition))
                    updatedOnUiThread = Dispatcher.UIThread.CheckAccess();
            };
            PumpUntilCompleted(Task.Run(() => preview.PositionChanged!(0.3)));
            Dispatcher.UIThread.RunJobs();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.Multiple(() =>
            {
                Assert.That(updatedOnUiThread, Is.True);
                Assert.That(waveform.PlaybackPosition, Is.EqualTo(1.35).Within(0.00001));
                Assert.That(waveform.IsEnabled, Is.False);
                Assert.That(window.FindControl<TextBlock>("PlaybackPositionText")!.Text, Does.StartWith(caption).And.Contain("1.350").And.Contain("3.000"));
            });
            PumpUntilCompleted(Task.WhenAll(vm.StopPreviewCommand.ExecuteAsync(null), playing));
            Assert.That(waveform.IsEnabled, Is.True);
            Assert.That(waveform.PlaybackPosition, Is.EqualTo(1.35).Within(0.00001));
        }, preview);
    }

    [Test]
    public void SpaceControlsPreviewWhileCaptureButtonIsFocusedAndIgnoresKeyRepeat()
    {
        FakePreview preview = new() { WaitForCancellation = true };
        WithEditor("zh_CN", ThemeManager.whiteGraySchemeName, (window, vm) =>
        {
            vm.SelectionStart = 0.5;
            vm.SelectionEnd = 2.5;
            Button capture = window.FindControl<Button>("CaptureRecentButton")!;
            int captureClicks = 0;
            capture.Click += (_, _) => captureClicks++;
            Assert.That(capture.Focus(), Is.True);
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.That(vm.IsPlaying, Is.True, "Holding Space must not immediately pause again.");
            Assert.That(window.FindControl<Button>("PreviewButton")!.Content, Is.EqualTo("暂停试听"));
            preview.PositionChanged!(0.7);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            PumpUntilCompleted(vm.TogglePreviewCommand.ExecutionTask!);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Assert.Multiple(() =>
            {
                Assert.That(vm.IsPaused, Is.True);
                Assert.That(vm.PlaybackPosition, Is.EqualTo(1.2).Within(0.00001));
                Assert.That(vm.SelectionStart, Is.EqualTo(0.5));
                Assert.That(vm.SelectionEnd, Is.EqualTo(2.5));
                Assert.That(captureClicks, Is.Zero);
                Assert.That(window.FindControl<Button>("PreviewButton")!.Content, Is.EqualTo("继续试听"));
            });
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Assert.That(vm.IsPlaying, Is.True);
            Assert.That(vm.PlaybackPosition, Is.EqualTo(1.2).Within(0.00001));
            PumpUntilCompleted(vm.StopPreviewCommand.ExecuteAsync(null));
        }, preview);
    }

    [Test]
    public void SpaceInTextAndNumericInputsDoesNotStartPreview()
    {
        WithEditor("zh_CN", ThemeManager.whiteGraySchemeName, (window, vm) =>
        {
            TextBox name = window.FindControl<TextBox>("LabelNameInput")!;
            Assert.That(name.Focus(), Is.True);
            name.CaretIndex = name.Text!.Length;
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyTextInput(" ");
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Assert.That(name.Text, Does.EndWith(" "));
            TextBox numeric = window.FindControl<NumericUpDown>("SelectionStartInput")!.GetVisualDescendants().OfType<TextBox>().Single();
            Assert.That(numeric.Focus(), Is.True);
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Assert.That(vm.IsPlaying, Is.False);
            Assert.That(vm.IsPaused, Is.False);
        }, new FakePreview { WaitForCancellation = true });
    }

    [Test]
    public void DisabledPreviewShortcutNeverActivatesCaptureOrStopsRecording()
    {
        FakeSource source = new();
        WithEditor("zh_CN", ThemeManager.whiteGraySchemeName, (window, vm) =>
        {
            Button capture = window.FindControl<Button>("CaptureRecentButton")!;
            Assert.That(capture.Focus(), Is.True);
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.That(vm.HasClip, Is.False);
            Assert.That(vm.IsBusy, Is.False);
            vm.ToggleRecordingCommand.Execute(null);
            Button record = window.FindControl<Button>("RecordButton")!;
            Assert.That(record.Focus(), Is.True);
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Assert.That(vm.IsRecording, Is.True);
            Assert.That(record.Content, Is.EqualTo("停止录制"));
            Assert.That(window.FindControl<TextBlock>("RecordingTimeText")!.IsVisible, Is.True);
            source.Emit(new byte[16000 * 4]);
            vm.ToggleRecordingCommand.Execute(null);
            PumpUntil(() => !vm.IsRecording);
            Assert.Multiple(() =>
            {
                Assert.That(vm.Duration, Is.EqualTo(1));
                Assert.That(vm.RecordingDuration, Is.EqualTo(1));
                Assert.That(record.Content, Is.EqualTo("开始录制"));
                Assert.That(window.FindControl<TextBlock>("RecordingTimeText")!.IsVisible, Is.False);
            });
        }, source: source, capture: false);
    }

    [Test]
    public void PausedClickAndCursorDragSeekWithoutAlteringSelectionAndBoundaryDragStillTrims()
    {
        FakePreview preview = new() { WaitForCancellation = true };
        WithEditor("en_US", ThemeManager.DarkModeSchemeName, (window, vm) =>
        {
            vm.SelectionStart = 0.3;
            vm.SelectionEnd = 2.7;
            PumpUntilCompleted(vm.TogglePreviewCommand.ExecuteAsync(null));
            preview.PositionChanged!(1.2);
            PumpUntilCompleted(vm.TogglePreviewCommand.ExecuteAsync(null));
            AudioWaveform waveform = window.FindControl<AudioWaveform>("Waveform")!;
            Point At(double seconds, double y = 80) => waveform.TranslatePoint(new Point(seconds / 3 * waveform.Bounds.Width, y), window)!.Value;
            window.MouseDown(At(0.6), MouseButton.Left);
            window.MouseUp(At(0.6), MouseButton.Left);
            Assert.That(vm.PlaybackPosition, Is.EqualTo(0.6).Within(0.02));
            window.MouseDown(At(0.6), MouseButton.Left);
            window.MouseMove(At(2.1));
            window.MouseUp(At(2.1), MouseButton.Left);
            Assert.That(vm.PlaybackPosition, Is.EqualTo(2.1).Within(0.02));
            Assert.That(vm.SelectionStart, Is.EqualTo(0.3));
            Assert.That(vm.SelectionEnd, Is.EqualTo(2.7));
            PumpUntilCompleted(vm.TogglePreviewCommand.ExecuteAsync(null));
            Assert.That(preview.Played!.DurationSeconds, Is.EqualTo(0.6).Within(0.02));
            PumpUntilCompleted(vm.TogglePreviewCommand.ExecuteAsync(null));
            // When the cursor overlaps a trim boundary, its top red handle remains draggable.
            vm.SeekPreviewCommand.Execute(0.3);
            window.MouseDown(At(0.3, 4), MouseButton.Left);
            window.MouseMove(At(0.9, 4));
            window.MouseUp(At(0.9, 4), MouseButton.Left);
            Assert.That(vm.SelectionStart, Is.EqualTo(0.3));
            Assert.That(vm.PlaybackPosition, Is.EqualTo(0.9).Within(0.02));
            window.MouseDown(At(0.3), MouseButton.Left);
            window.MouseMove(At(0.45));
            window.MouseUp(At(0.45), MouseButton.Left);
            Assert.That(vm.SelectionStart, Is.EqualTo(0.45).Within(0.02));
            Assert.That(vm.IsPaused, Is.False);
            PumpUntilCompleted(vm.TogglePreviewCommand.ExecuteAsync(null));
            Assert.That(vm.PlaybackPosition, Is.EqualTo(0.45).Within(0.02));
            PumpUntilCompleted(vm.StopPreviewCommand.ExecuteAsync(null));
        }, preview);
    }

    [Test]
    [Explicit("Creates preview images for visual QA using the safe direct Skia encoder.")]
    [Category("Manual")]
    public void RenderAudioLabelEditorPreviews()
    {
        foreach ((string locale, string scheme, string name, bool recording) in new[]
        {
            ("zh_CN", ThemeManager.whiteGraySchemeName, "audio-label-light", false),
            ("zh_CN", ThemeManager.DarkModeSchemeName, "audio-label-dark", false),
            ("en_US", ThemeManager.whiteGraySchemeName, "audio-label-english", false),
            ("zh_CN", ThemeManager.whiteGraySchemeName, "audio-label-recording", true),
            ("en_US", ThemeManager.whiteGraySchemeName, "audio-label-recording-english", true)
        })
        {
            FakeSource source = new();
            FakePreview preview = new() { WaitForCancellation = true };
            WithEditor(locale, scheme, (window, vm) =>
            {
                window.Height = 720;
                vm.SelectionStart = 1.05;
                vm.SelectionEnd = 1.85;
                if (recording)
                {
                    window.Width = window.MinWidth;
                    vm.ToggleRecordingCommand.Execute(null);
                    source.Emit(new byte[16000 * 4]);
                    PumpUntil(() => vm.RecordingDuration > 0);
                }
                else
                {
                    Task playing = vm.PlaySelectionCommand.ExecuteAsync(null);
                    preview.PositionChanged!(0.3);
                    Dispatcher.UIThread.RunJobs();
                    PumpUntilCompleted(Task.WhenAll(vm.TogglePreviewCommand.ExecuteAsync(null), playing));
                }
                using WriteableBitmap frame = window.CaptureRenderedFrame()!;
                string output = Path.Combine(Path.GetTempPath(), "easycon-audio-label-qa");
                Directory.CreateDirectory(output);
                string path = Path.Combine(output, name + ".png");
                using ILockedFramebuffer buffer = frame.Lock();
                SKColorType colorType = buffer.Format == PixelFormat.Rgba8888 ? SKColorType.Rgba8888 : SKColorType.Bgra8888;
                SKImageInfo info = new(buffer.Size.Width, buffer.Size.Height, colorType, SKAlphaType.Premul);
                using SKImage image = SKImage.FromPixels(info, buffer.Address, buffer.RowBytes);
                using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
                using FileStream stream = File.Create(path);
                data.SaveTo(stream);
                TestContext.Out.WriteLine(path);
            }, preview, source);
        }
    }

    private static void WithEditor(string locale, string scheme, Action<AudioLabelEditorWindow, AudioLabelEditorViewModel> action,
        FakePreview? preview = null, FakeSource? source = null, bool capture = true)
    {
        ResourceDictionary dictionary = (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri($"avares://EasyCon2.Avalonia/Resources/Locales/{locale}.axaml"));
        Application.Current!.Resources.MergedDictionaries.Add(dictionary);
        Func<string, string>? resolver = L10nBridge.Resolver;
        L10nBridge.Resolver = L10n.T;
        ThemeManager.Instance.ApplyColorScheme(scheme);
        source ??= new();
        using AudioHistoryBuffer history = new(source);
        using AudioLabelEditorViewModel vm = new(history, preview ?? new FakePreview(), new FakeDialogs(), AvaloniaUiDispatcher.Instance);
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
            if (capture)
                PumpUntilCompleted(vm.CaptureRecentCommand.ExecuteAsync(null));
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

    private static void PumpUntilCompleted(Task task)
    {
        PumpUntil(() => task.IsCompleted);
        Assert.That(task.IsCompletedSuccessfully, Is.True);
    }

    private static void PumpUntil(Func<bool> completed)
    {
        Stopwatch timeout = Stopwatch.StartNew();
        while (!completed() && timeout.Elapsed < TimeSpan.FromSeconds(5))
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Yield();
        }
        Dispatcher.UIThread.RunJobs();
        Assert.That(completed(), Is.True);
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
        public bool WaitForCancellation { get; init; }
        public Action<double>? PositionChanged { get; private set; }
        public AudioClip? Played { get; private set; }
        public Task PlayAsync(AudioClip clip, CancellationToken cancellationToken, Action<double>? positionChanged = null)
        {
            PositionChanged = positionChanged;
            Played = clip;
            return WaitForCancellation ? Task.Delay(Timeout.Infinite, cancellationToken) : Task.CompletedTask;
        }
    }

    private sealed class FakeDialogs : IDialogService
    {
        public Task<string?> OpenFolderAsync(string title, string? suggestedStartPath = null) => Task.FromResult<string?>(null);
        public Task<IReadOnlyList<string>> OpenFilesAsync(string title, IReadOnlyList<FileDialogFilter>? filters = null, string? suggestedStartPath = null) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> SaveFileAsync(string title, string defaultExtension, IReadOnlyList<FileDialogFilter>? filters = null, string? suggestedFileName = null) => Task.FromResult<string?>(null);
        public Task<DialogColor?> PickColorAsync(DialogColor current) => Task.FromResult<DialogColor?>(null);
        public Task<string[]?> ShowScriptArgsDialogAsync() => Task.FromResult<string[]?>(null);
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(false);
        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;
    }
}