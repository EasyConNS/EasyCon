using EasyCon.Core.Capabilities;
using EasyCon.Core.Maa;
using EasyCon.Core.Services;
using EasyCon2.Avalonia.DrawingBoard;
using EasyCon2.Avalonia.Models;
using EasyCon2.Avalonia.Services;
using EasyScript;

namespace EasyCon2.Avalonia.Core.Tests;

/// <summary>喷射战士画板：扫描与移动路径对齐 1.6.3 WinForms 版 DrawingBoard，并锁定其后修复的行为。</summary>
[TestFixture]
public class DrawingBoardTests
{
    private const string LeaseRelease = "lease-release";

    private static readonly DrawingBoardSettings FullCanvas = new(0, 0, 0, 0, 320, 120, false);

    [Test]
    public void SearchLines_SplitsDarkRunsPerRow()
    {
        PixelImage image = Canvas((1, 2), (1, 3), (1, 7));

        List<List<DrawingLine>> rows = SplatoonDrawing.SearchLines(image, FullCanvas);

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].Select(Describe), Is.EqualTo(new[] { "1:2-3", "1:7-7" }));
    }

    [Test]
    public void SearchLines_RunReachingRowEnd_EndsAtLastColumn()
    {
        PixelImage image = Canvas((0, 318), (0, 319));

        List<List<DrawingLine>> rows = SplatoonDrawing.SearchLines(image, FullCanvas);

        Assert.That(rows[0].Select(Describe), Is.EqualTo(new[] { "0:318-319" }));
    }

    [Test]
    public void SearchLines_SingleDotAtColumnZero_StaysSinglePixel()
    {
        // 旧版用 end.Y == 0 判断"线段延续到行尾"，第 0 列的单个墨点会被画成整行
        PixelImage image = Canvas((0, 0));

        List<List<DrawingLine>> rows = SplatoonDrawing.SearchLines(image, FullCanvas);

        Assert.That(rows[0].Select(Describe), Is.EqualTo(new[] { "0:0-0" }));
    }

    [Test]
    public void SearchLines_RunReachingRegionEnd_EndsAtRegionLastColumn()
    {
        PixelImage image = Canvas((0, 3), (0, 4), (0, 5));

        List<List<DrawingLine>> rows = SplatoonDrawing.SearchLines(image, FullCanvas with { Col = 5 });

        Assert.That(rows[0].Select(Describe), Is.EqualTo(new[] { "0:3-4" }));
    }

    [Test]
    public void SearchLines_Reverse_DrawsBrightPixels()
    {
        PixelImage image = Canvas((5, 10));

        List<List<DrawingLine>> rows = SplatoonDrawing.SearchLines(image, FullCanvas with { Row = 1, Col = 3, Reverse = true });

        Assert.That(rows[0].Select(Describe), Is.EqualTo(new[] { "0:0-2" }));
    }

    [Test]
    public void SearchLines_TransparentPixels_CountAsWhite()
    {
        byte[] rgba = White(320, 120);
        SetPixel(rgba, 0, 2, 0, 0, 0, 255);
        SetPixel(rgba, 0, 5, 0, 0, 0, 0);     // 全透明黑：视为白
        SetPixel(rgba, 0, 6, 0, 0, 0, 200);   // 大部分不透明的黑：仍是墨点
        PixelImage image = new(320, 120, rgba);

        List<List<DrawingLine>> rows = SplatoonDrawing.SearchLines(image, FullCanvas);
        List<List<DrawingLine>> reversed = SplatoonDrawing.SearchLines(image, FullCanvas with { Col = 8, Reverse = true });

        Assert.That(rows[0].Select(Describe), Is.EqualTo(new[] { "0:2-2", "0:6-6" }));
        Assert.That(reversed[0].Select(Describe), Is.EqualTo(new[] { "0:0-1", "0:3-5", "0:7-7" }));
    }

    [Test]
    public void SearchLines_ClampsRegionToCanvas()
    {
        PixelImage image = Canvas((119, 319), (100, 300));

        List<List<DrawingLine>> rows = SplatoonDrawing.SearchLines(image, FullCanvas with { StartRow = 100, StartCol = 300 });

        Assert.That(rows.SelectMany(r => r).Select(Describe), Is.EqualTo(new[] { "100:300-300", "119:319-319" }));
    }

    [Test]
    public void Estimate_SumsEveryWaitOfTheDrawSequence()
    {
        // 13 次方向点按 × (50 + 45)，外加每段线：两次移动后各 2 次、落笔后 1 次、抬笔后 1 次延迟，两段共 12 × 45
        List<List<DrawingLine>> rows = SplatoonDrawing.SearchLines(Canvas((0, 5), (0, 6), (2, 1)), FullCanvas);

        TimeSpan elapsed = SplatoonDrawing.Estimate(rows, 45, 50);

        Assert.That(elapsed, Is.EqualTo(TimeSpan.FromMilliseconds(13 * 95 + 12 * 45)));
    }

    [Test]
    public void Estimate_MatchesWaitsActuallyPerformedByDraw()
    {
        PixelImage image = Canvas((0, 5), (0, 6), (2, 1), (7, 300), (7, 302), (90, 0));
        RecordingPad pad = new();
        long waited = 0;

        new SplatoonDrawing(pad, 45, 50, (milliseconds, _) => waited += milliseconds)
            .Draw(SplatoonDrawing.SearchLines(image, FullCanvas), CancellationToken.None);
        TimeSpan estimated = SplatoonDrawing.Estimate(SplatoonDrawing.SearchLines(image, FullCanvas), 45, 50);

        Assert.That(estimated, Is.EqualTo(TimeSpan.FromMilliseconds(waited + pad.ClickDurations)));
    }

    [Test]
    public void FormatEvaluation_UsesMinutes()
    {
        Assert.That(SplatoonDrawing.FormatEvaluation(TimeSpan.FromSeconds(95)), Is.EqualTo("耗时：" + 1.583.ToString("0.00") + "mins"));
    }

    [Test]
    public void Draw_EmitsLegacyKeySequence()
    {
        List<List<DrawingLine>> rows = SplatoonDrawing.SearchLines(Canvas((0, 5), (0, 6), (2, 1)), FullCanvas);
        RecordingPad pad = new();

        new SplatoonDrawing(pad, 0, 0).Draw(rows, CancellationToken.None);

        List<string> expected = [];
        expected.AddRange(Clicks(GamePadKey.RIGHT, 5));
        expected.Add("press A");
        expected.AddRange(Clicks(GamePadKey.RIGHT, 1));
        expected.Add("release A");
        // 第二行：行先下移两格，再左移回第 1 列
        expected.AddRange(Clicks(GamePadKey.DOWN, 2));
        expected.AddRange(Clicks(GamePadKey.LEFT, 5));
        expected.Add("press A");
        expected.Add("release A");
        Assert.That(pad.Events, Is.EqualTo(expected));
    }

    [Test]
    public void Draw_CancelledBeforeStart_SendsNothing()
    {
        List<List<DrawingLine>> rows = SplatoonDrawing.SearchLines(Canvas((0, 5)), FullCanvas);
        RecordingPad pad = new();

        Assert.Throws<OperationCanceledException>(() => new SplatoonDrawing(pad, 0, 0).Draw(rows, new CancellationToken(true)));
        Assert.That(pad.Events, Is.Empty);
    }

    [Test]
    public void Draw_CancelledWhileKeyHeld_StopsPromptly()
    {
        List<List<DrawingLine>> rows = SplatoonDrawing.SearchLines(Canvas((0, 100), (0, 101)), FullCanvas);
        using CancellationTokenSource cts = new();
        RecordingPad pad = new() { WaitForDuration = true, OnKey = cts.Cancel };
        System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();

        Assert.Throws<OperationCanceledException>(() => new SplatoonDrawing(pad, 0, 10_000).Draw(rows, cts.Token));

        Assert.That(elapsed.ElapsedMilliseconds, Is.LessThan(2_000));
        Assert.That(pad.Events, Is.EqualTo(new[] { "click RIGHT" }));
    }

    [Test]
    public async Task Stop_WaitsForDrawToExitThenReenablesStart()
    {
        Fixture f = new(Canvas((0, 100)));
        await f.Vm.LoadPictureFileAsync("x.png");
        f.Vm.DelayText = "0";
        f.Vm.DurationText = "10000";
        TaskCompletionSource firstKey = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Pad.WaitForDuration = true;
        f.Pad.OnKey = () => firstKey.TrySetResult();

        await f.Vm.StartCommand.ExecuteAsync(null);
        await firstKey.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(f.Vm.IsStartEnabled, Is.False);
        await f.Vm.StopCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(f.Vm.DrawTask!.IsCompleted, Is.True);
        Assert.That(f.Vm.IsStartEnabled, Is.True);
        Assert.That(f.Dialogs.Messages, Is.Empty, "用户停止不应弹错误");
        int count = f.Pad.Events.Count;
        await Task.Delay(100);
        Assert.That(f.Pad.Events, Has.Count.EqualTo(count), "停止后不应再有按键");
        Assert.That(f.Pad.Events.Last(), Is.EqualTo(LeaseRelease));
    }

    [TestCase(100, 50)]
    [TestCase(320, 100)]
    [TestCase(300, 120)]
    public async Task Load_WrongSize_IsRejected(int width, int height)
    {
        Fixture f = new(new PixelImage(width, height, White(width, height)));

        await f.Vm.LoadPictureFileAsync("x.png");
        await f.Vm.EvaluateCommand.ExecuteAsync(null);

        Assert.That(f.Dialogs.Messages, Is.EqualTo(new[] { "图片不是320*120大小，请重新修改", "未加载图片" }));
        Assert.That(f.Vm.PicturePng, Is.Null);
    }

    [Test]
    public async Task Start_Disconnected_Warns()
    {
        Fixture f = new(Canvas());
        f.Runner.Connected = false;

        await f.Vm.StartCommand.ExecuteAsync(null);

        Assert.That(f.Dialogs.Messages, Is.EqualTo(new[] { "串口未连接" }));
        Assert.That(f.Vm.DrawTask, Is.Null);
    }

    [Test]
    public async Task Start_WithoutImage_WarnsWithoutStarting()
    {
        Fixture f = new(Canvas());

        await f.Vm.StartCommand.ExecuteAsync(null);

        Assert.That(f.Dialogs.Messages, Is.EqualTo(new[] { "未加载图片" }));
        Assert.That(f.Vm.DrawTask, Is.Null);
        Assert.That(f.Vm.IsStartEnabled, Is.True);
    }

    [TestCase("abc", "0", "0", "320", "120", "延迟应为 0～10000 的整数")]
    [TestCase("45", "-1", "0", "320", "120", "起始列应为 0～319 的整数")]
    [TestCase("45", "0", "120", "320", "120", "起始行应为 0～119 的整数")]
    [TestCase("45", "0", "0", "0", "120", "列数应为 1～320 的整数")]
    [TestCase("45", "0", "0", "320", "", "行数应为 1～120 的整数")]
    public async Task InvalidSettings_AreRejectedWithFieldName(string delay, string startCol, string startRow, string width, string height, string expected)
    {
        Fixture f = new(Canvas((0, 1)));
        await f.Vm.LoadPictureFileAsync("x.png");
        f.Vm.DelayText = delay;
        f.Vm.StartPosXText = startCol;
        f.Vm.StartPosYText = startRow;
        f.Vm.WidthText = width;
        f.Vm.HeightText = height;

        await f.Vm.StartCommand.ExecuteAsync(null);
        await f.Vm.EvaluateCommand.ExecuteAsync(null);

        Assert.That(f.Dialogs.Messages, Is.EqualTo(new[] { expected, expected }));
        Assert.That(f.Vm.DrawTask, Is.Null);
        Assert.That(f.Vm.EvaluateText, Is.EqualTo("耗时："));
    }

    [Test]
    public async Task DurationZero_IsRejected()
    {
        Fixture f = new(Canvas((0, 1)));
        await f.Vm.LoadPictureFileAsync("x.png");
        f.Vm.DurationText = "0";

        await f.Vm.EvaluateCommand.ExecuteAsync(null);

        Assert.That(f.Dialogs.Messages, Is.EqualTo(new[] { "按键持续应为 1～10000 的整数" }));
    }

    [Test]
    public async Task Start_DrawsAndReenablesStart()
    {
        Fixture f = new(Canvas((0, 1)));
        await f.Vm.LoadPictureFileAsync("x.png");
        f.Vm.DelayText = "0";
        f.Vm.DurationText = "1";

        await f.Vm.StartCommand.ExecuteAsync(null);
        await f.Vm.DrawTask!;

        Assert.That(f.Vm.PicturePng, Is.Not.Null);
        Assert.That(f.Pad.Events, Does.Contain("press A"));
        Assert.That(f.Pad.Events.Last(), Is.EqualTo(LeaseRelease));
        Assert.That(f.Vm.IsStartEnabled, Is.True);
    }

    [Test]
    public async Task Dispose_StopsRunningDrawAndReleasesKeys()
    {
        Fixture f = new(Canvas((0, 100)));
        await f.Vm.LoadPictureFileAsync("x.png");
        f.Vm.DelayText = "0";
        f.Vm.DurationText = "10000";
        TaskCompletionSource firstKey = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Pad.WaitForDuration = true;
        f.Pad.OnKey = () => firstKey.TrySetResult();

        await f.Vm.StartCommand.ExecuteAsync(null);
        await firstKey.Task.WaitAsync(TimeSpan.FromSeconds(5));
        f.Vm.Dispose();
        await f.Vm.DrawTask!.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(f.Pad.Events.Last(), Is.EqualTo(LeaseRelease));
    }

    [Test]
    public async Task Start_DeviceBusy_ReportsAndReenablesStart()
    {
        Fixture f = new(Canvas((0, 1)));
        await f.Vm.LoadPictureFileAsync("x.png");
        f.Runner.Failure = new ControllerOperationException("Busy", "busy: 设备已有运行或正在收尾");

        await f.Vm.StartCommand.ExecuteAsync(null);
        await f.Vm.DrawTask!;

        Assert.That(f.Vm.IsStartEnabled, Is.True);
        Assert.That(f.Dialogs.Messages, Is.EqualTo(new[] { "画图中断: 手柄正被脚本或其他任务占用，请先停止后再画" }));
        Assert.That(f.Pad.Events, Is.Empty);
    }

    [Test]
    public async Task Start_DeviceLostMidDraw_ReportsDisconnect()
    {
        Fixture f = new(Canvas((0, 1)));
        await f.Vm.LoadPictureFileAsync("x.png");
        f.Vm.DelayText = "0";
        f.Vm.DurationText = "1";
        f.Pad.OnKey = () => throw new OperationCanceledException();

        await f.Vm.StartCommand.ExecuteAsync(null);
        await f.Vm.DrawTask!;

        Assert.That(f.Vm.IsStartEnabled, Is.True);
        Assert.That(f.Dialogs.Messages, Is.EqualTo(new[] { "画图中断: 手柄连接已断开" }));
        Assert.That(f.Pad.Events.Last(), Is.EqualTo(LeaseRelease));
    }

    [TestCase(0, 0, "当前位置:0,0")]
    [TestCase(411, 141, "当前位置:160,60")]
    [TestCase(821.9, 281.9, "当前位置:319,119")]
    [TestCase(822, 282, "当前位置:319,119")]
    public void PicturePressed_ScalesToCanvasCoordinates(double x, double y, string expected)
    {
        Fixture f = new(Canvas());

        f.Vm.OnPicturePressed(x, y, 822, 282);

        Assert.That(f.Vm.CurPosText, Is.EqualTo(expected));
    }

    private static string Describe(DrawingLine line) => $"{line.start.X}:{line.start.Y}-{line.end.Y}";

    private static IEnumerable<string> Clicks(GamePadKey key, int count)
        => Enumerable.Repeat("click " + key, count);

    private static byte[] White(int width, int height) => Enumerable.Repeat((byte)255, width * height * 4).ToArray();

    private static void SetPixel(byte[] rgba, int row, int col, byte r, byte g, byte b, byte a)
    {
        int i = (row * 320 + col) * 4;
        rgba[i] = r;
        rgba[i + 1] = g;
        rgba[i + 2] = b;
        rgba[i + 3] = a;
    }

    /// <summary>320×120 白底，给定 (行, 列) 处为黑点。</summary>
    private static PixelImage Canvas(params (int Row, int Col)[] dark)
    {
        byte[] rgba = White(320, 120);
        foreach ((int row, int col) in dark)
        {
            int i = (row * 320 + col) * 4;
            rgba[i] = rgba[i + 1] = rgba[i + 2] = 0;
        }
        return new PixelImage(320, 120, rgba);
    }

    private sealed class Fixture
    {
        public Fixture(PixelImage image)
        {
            Runner = new FakeRunner(Pad);
            Vm = new DrawingBoardViewModel(Runner, Dialogs, new FakeImages(image), SynchronousUiDispatcher.Instance, new NullLog());
        }

        public RecordingPad Pad { get; } = new();
        public FakeRunner Runner { get; }
        public FakeDialogs Dialogs { get; } = new();
        public DrawingBoardViewModel Vm { get; }
    }

    /// <summary>模拟运行租约：回调结束（含取消与异常）后记录一次中性释放。</summary>
    private sealed class FakeRunner(RecordingPad pad) : IDrawingRunner
    {
        public bool Connected { get; set; } = true;
        public Exception? Failure { get; set; }
        public bool IsConnected => Connected;

        public void Run(Action<IPadInput, CancellationToken> draw, CancellationToken token)
        {
            if (Failure != null)
                throw Failure;
            try
            {
                draw(pad, token);
            }
            finally
            {
                pad.Add(LeaseRelease);
            }
        }
    }

    private sealed class RecordingPad : IPadInput
    {
        private readonly List<string> _events = [];
        private long _clickDurations;

        /// <summary>点按时真实等待按键持续（可被取消），模拟设备侧 pulse。</summary>
        public bool WaitForDuration { get; set; }
        public Action? OnKey { get; set; }
        public long ClickDurations => Interlocked.Read(ref _clickDurations);

        public List<string> Events
        {
            get
            {
                lock (_events)
                    return [.. _events];
            }
        }

        public void ClickButtons(GamePadKey key, int duration, CancellationToken token)
        {
            Add("click " + key);
            Interlocked.Add(ref _clickDurations, duration);
            OnKey?.Invoke();
            if (WaitForDuration && token.WaitHandle.WaitOne(duration))
                throw new OperationCanceledException(token);
        }

        public void PressButtons(GamePadKey key)
        {
            Add("press " + key);
            OnKey?.Invoke();
        }

        public void ReleaseButtons(GamePadKey key) => Add("release " + key);
        public void ClickStick(GamePadKey key, byte x, byte y, int duration, CancellationToken token) => Add("stick " + key);
        public void SetStick(GamePadKey key, byte x, byte y) => Add("stick " + key);
        public void ChangeAmiibo(uint index) { }

        public void Add(string e)
        {
            lock (_events)
                _events.Add(e);
        }
    }

    private sealed class FakeImages(PixelImage image) : IImageProcessor
    {
        public byte[]? Crop(byte[] sourceBytes, int x, int y, int width, int height) => null;
        public byte[]? LoadAsPng(string filePath) => [1];
        public PixelImage? LoadPixels(string filePath) => image;
    }

    private sealed class FakeDialogs : IDialogService
    {
        public List<string> Messages { get; } = [];
        public Task<IReadOnlyList<string>> OpenFilesAsync(string title, IReadOnlyList<FileDialogFilter>? filters = null, string? suggestedStartPath = null)
            => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> SaveFileAsync(string title, string defaultExtension, IReadOnlyList<FileDialogFilter>? filters = null, string? suggestedFileName = null)
            => Task.FromResult<string?>(null);
        public Task<string?> OpenFolderAsync(string title, string? suggestedStartPath = null) => Task.FromResult<string?>(null);
        public Task<DialogColor?> PickColorAsync(DialogColor current) => Task.FromResult<DialogColor?>(null);
        public Task<string[]?> ShowScriptArgsDialogAsync() => Task.FromResult<string[]?>(null);
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(false);
        public Task ShowMessageAsync(string title, string message)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class NullLog : ILogService
    {
        public event Action<string?, string?>? LogAppended
        {
            add { }
            remove { }
        }
        public void AddLog(string message, string? color = null) { }
        public void Clear() { }
        public void Print(string message, bool newline = true) { }
        public void Alert(string message) { }
        public string ReadLine() => "";
        public bool TryReadLine(out string line)
        {
            line = "";
            return false;
        }
    }
}