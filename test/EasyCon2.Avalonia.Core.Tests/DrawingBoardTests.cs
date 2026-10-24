using EasyCon.Core.Services;
using EasyCon2.Avalonia.DrawingBoard;
using EasyCon2.Avalonia.Models;
using EasyCon2.Avalonia.Services;
using EasyDevice;

namespace EasyCon2.Avalonia.Core.Tests;

/// <summary>喷射战士画板：断言与 1.6.3 WinForms 版 DrawingBoard 的行为一致（含旧版自身的取舍）。</summary>
[TestFixture]
public class DrawingBoardTests
{
    private static readonly string A = ECKeyUtil.Button(SwitchButton.A).Name;
    private static readonly string Down = ECKeyUtil.HAT(SwitchHAT.BOTTOM).Name;
    private static readonly string Right = ECKeyUtil.HAT(SwitchHAT.RIGHT).Name;
    private static readonly string Left = ECKeyUtil.HAT(SwitchHAT.LEFT).Name;

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
    public void SearchLines_LastRunEndingAtColumnZero_ExtendsToRowEnd_LikeLegacy()
    {
        // 旧版用 end.Y == 0 判断"线段延续到行尾"，第 0 列的单个墨点因此被画成整行
        PixelImage image = Canvas((0, 0));

        List<List<DrawingLine>> rows = SplatoonDrawing.SearchLines(image, FullCanvas);

        Assert.That(rows[0].Select(Describe), Is.EqualTo(new[] { "0:0-319" }));
    }

    [Test]
    public void SearchLines_Reverse_DrawsBrightPixels()
    {
        PixelImage image = Canvas((5, 10));

        List<List<DrawingLine>> rows = SplatoonDrawing.SearchLines(image, FullCanvas with { Row = 1, Col = 3, Reverse = true });

        Assert.That(rows[0].Select(Describe), Is.EqualTo(new[] { "0:0-2" }));
    }

    [Test]
    public void SearchLines_ClampsRegionToCanvas()
    {
        PixelImage image = Canvas((119, 319), (100, 300));

        List<List<DrawingLine>> rows = SplatoonDrawing.SearchLines(image, FullCanvas with { StartRow = 100, StartCol = 300 });

        Assert.That(rows.SelectMany(r => r).Select(Describe), Is.EqualTo(new[] { "100:300-300", "119:319-319" }));
    }

    [Test]
    public void Evaluate_CountsColumnMovesOnly()
    {
        List<List<DrawingLine>> rows = SplatoonDrawing.SearchLines(Canvas((0, 5), (0, 6), (2, 1)), FullCanvas);

        int points = SplatoonDrawing.EvaluatePoints(rows);

        Assert.That(points, Is.EqualTo(11));
        Assert.That(SplatoonDrawing.FormatEvaluation(1000, 45, 50), Is.EqualTo("耗时：" + 1.583.ToString("0.00") + "mins"));
    }

    [Test]
    public void Draw_EmitsLegacyKeySequence()
    {
        List<List<DrawingLine>> rows = SplatoonDrawing.SearchLines(Canvas((0, 5), (0, 6), (2, 1)), FullCanvas);
        RecordingPad pad = new();

        bool completed = new SplatoonDrawing(pad, 0, 0).Draw(rows, CancellationToken.None);

        List<string> expected = [];
        expected.AddRange(Clicks(Right, 5));
        expected.Add("down " + A);
        expected.AddRange(Clicks(Right, 1));
        expected.Add("reset");
        // 第二行：行先下移两格，再左移回第 1 列
        expected.AddRange(Clicks(Down, 2));
        expected.AddRange(Clicks(Left, 5));
        expected.Add("down " + A);
        expected.Add("reset");
        expected.Add("reset");
        expected.Add("reset");
        Assert.That(completed, Is.True);
        Assert.That(pad.Events, Is.EqualTo(expected));
    }

    [Test]
    public void Draw_Cancelled_StopsWithoutFinalReset()
    {
        List<List<DrawingLine>> rows = SplatoonDrawing.SearchLines(Canvas((0, 5)), FullCanvas);
        RecordingPad pad = new();

        bool completed = new SplatoonDrawing(pad, 0, 0).Draw(rows, new CancellationToken(true));

        Assert.That(completed, Is.False);
        Assert.That(pad.Events, Is.Empty);
    }

    [Test]
    public async Task Load_WrongSize_WarnsAndSkipsPreview()
    {
        Fixture f = new(new PixelImage(100, 50, White(100, 50)));

        await f.Vm.LoadPictureFileAsync("x.png");

        Assert.That(f.Dialogs.Messages, Is.EqualTo(new[] { "图片不是320*120大小，建议重新修改" }));
        Assert.That(f.Vm.PicturePng, Is.Null);
    }

    [Test]
    public async Task Start_Disconnected_Warns()
    {
        Fixture f = new(Canvas());
        f.Pad.Connected = false;

        await f.Vm.StartCommand.ExecuteAsync(null);

        Assert.That(f.Dialogs.Messages, Is.EqualTo(new[] { "串口未连接" }));
        Assert.That(f.Vm.DrawTask, Is.Null);
    }

    [Test]
    public async Task Start_WithoutImage_WarnsAndReenablesStart()
    {
        Fixture f = new(Canvas());

        await f.Vm.StartCommand.ExecuteAsync(null);
        await f.Vm.DrawTask!;

        Assert.That(f.Dialogs.Messages, Is.EqualTo(new[] { "未加载图片" }));
        Assert.That(f.Vm.IsStartEnabled, Is.True);
    }

    [Test]
    public async Task Start_DrawsAndReenablesStart()
    {
        Fixture f = new(Canvas((0, 1)));
        await f.Vm.LoadPictureFileAsync("x.png");
        f.Vm.DelayText = "0";
        f.Vm.DurationText = "0";

        await f.Vm.StartCommand.ExecuteAsync(null);
        await f.Vm.DrawTask!;

        Assert.That(f.Vm.PicturePng, Is.Not.Null);
        Assert.That(f.Pad.Events, Does.Contain("down " + A));
        Assert.That(f.Vm.IsStartEnabled, Is.True);
    }

    [Test]
    public void PicturePressed_ShowsHalvedCoordinates()
    {
        Fixture f = new(Canvas());

        f.Vm.OnPicturePressed(201, 81);

        Assert.That(f.Vm.CurPosText, Is.EqualTo("当前位置:100,40"));
    }

    private static string Describe(DrawingLine line) => $"{line.start.X}:{line.start.Y}-{line.end.Y}";

    private static IEnumerable<string> Clicks(string key, int count)
        => Enumerable.Range(0, count).SelectMany(_ => new[] { "down " + key, "up " + key });

    private static byte[] White(int width, int height) => Enumerable.Repeat((byte)255, width * height * 4).ToArray();

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
            Vm = new DrawingBoardViewModel(Pad, Dialogs, new FakeImages(image), SynchronousUiDispatcher.Instance, new NullLog());
        }

        public RecordingPad Pad { get; } = new();
        public FakeDialogs Dialogs { get; } = new();
        public DrawingBoardViewModel Vm { get; }
    }

    private sealed class RecordingPad : IDrawingPad
    {
        public bool Connected { get; set; } = true;
        public List<string> Events { get; } = [];
        public bool IsConnected() => Connected;
        public void Down(ECKey key) => Events.Add("down " + key.Name);
        public void Up(ECKey key) => Events.Add("up " + key.Name);
        public void Reset() => Events.Add("reset");
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