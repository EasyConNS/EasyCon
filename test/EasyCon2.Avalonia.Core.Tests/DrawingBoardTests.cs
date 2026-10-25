using EasyCon.Core.Capabilities;
using EasyCon.Core.Maa;
using EasyCon.Core.Services;
using EasyCon2.Avalonia.DrawingBoard;
using EasyCon2.Avalonia.Localization;
using EasyCon2.Avalonia.Models;
using EasyCon2.Avalonia.Services;
using EasyScript;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace EasyCon2.Avalonia.Core.Tests;

/// <summary>喷射战士画板：扫描与移动路径对齐 1.6.3 WinForms 版 DrawingBoard，并锁定其后修复的行为。</summary>
[TestFixture]
[NonParallelizable]
public class DrawingBoardTests
{
    private const string LeaseRelease = "lease-release";

    /// <summary>文案断言按真实的 zh_CN 资源进行（L10nBridge 未接宿主时只回显键名）。</summary>
    [SetUp]
    public void UseChineseLocale() => L10nBridge.Resolver = Locale("zh_CN").GetValueOrDefault;

    [TearDown]
    public void ResetLocale() => L10nBridge.Resolver = null;

    [Test]
    public void LocaleKeys_UsedByDrawingBoard_ExistInBothLanguages()
    {
        string drawingBoard = Path.Combine(RepoRoot(), "src", "EasyCon2.Avalonia", "DrawingBoard");
        string sources = string.Concat(Directory.GetFiles(drawingBoard).Select(File.ReadAllText))
            + File.ReadAllText(Path.Combine(RepoRoot(), "src", "EasyCon2.Avalonia", "Shell", "FeatureCenterPage.axaml"));
        string[] used = Regex.Matches(sources, @"Text\.(?:DrawingBoard\.\w+|Feature\.(?:Splatoon|FreeDraw))")
            .Select(m => m.Value).Distinct().ToArray();
        Dictionary<string, string> zh = Locale("zh_CN");
        Dictionary<string, string> en = Locale("en_US");

        Assert.That(used, Has.Length.GreaterThan(20));
        Assert.That(used.Where(key => !zh.ContainsKey(key)), Is.Empty, "zh_CN 缺少键");
        Assert.That(used.Where(key => !en.ContainsKey(key)), Is.Empty, "en_US 缺少键");
        Assert.That(en.Keys.Where(k => k.StartsWith("Text.DrawingBoard.")).Order(),
            Is.EqualTo(zh.Keys.Where(k => k.StartsWith("Text.DrawingBoard.")).Order()));
    }

    [Test]
    public async Task StatusTexts_FollowLanguageSwitch()
    {
        Fixture f = new(Canvas((0, 1)));
        await f.Vm.LoadPictureFileAsync("x.png");
        f.Vm.OnPicturePressed(411, 141, 822, 282);
        await f.Vm.EvaluateCommand.ExecuteAsync(null);
        List<string> changed = [];
        f.Vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        L10nBridge.Resolver = Locale("en_US").GetValueOrDefault;
        f.Vm.RefreshLocalizedTexts();

        Assert.That(changed, Is.SupersetOf(new[] { "CurPosText", "EvaluateText", "ProgressText" }));
        Assert.That(f.Vm.CurPosText, Is.EqualTo("Position: 160,60"));
        Assert.That(f.Vm.EvaluateText, Does.StartWith("Time: ").And.EndWith(" min"));
    }

    [Test]
    public async Task Evaluate_ShowsMinutes()
    {
        Fixture f = new(Canvas((0, 1)));
        await f.Vm.LoadPictureFileAsync("x.png");
        Assert.That(f.Vm.EvaluateText, Is.EqualTo("耗时："));

        await f.Vm.EvaluateCommand.ExecuteAsync(null);

        // 1 次点按 × (50 + 45) + 落笔前后两次移动各 2 次延迟 + 落笔、抬笔各 1 次延迟 = 95 + 6 × 45 = 365 ms
        Assert.That(f.Vm.EvaluateText, Is.EqualTo("耗时：" + (365 / 60000.0).ToString("0.00") + "mins"));
    }

    private static readonly DrawingBoardSettings FullCanvas = new(0, 0, 0, 0, 320, 120);

    [Test]
    public void SearchLines_SplitsDarkRunsPerRow()
    {
        PixelImage image = Canvas((1, 2), (1, 3), (1, 7));

        List<List<DrawingLine>> rows = Scan(image, FullCanvas);

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].Select(Describe), Is.EqualTo(new[] { "1:2-3", "1:7-7" }));
    }

    [Test]
    public void SearchLines_RunReachingRowEnd_EndsAtLastColumn()
    {
        PixelImage image = Canvas((0, 318), (0, 319));

        List<List<DrawingLine>> rows = Scan(image, FullCanvas);

        Assert.That(rows[0].Select(Describe), Is.EqualTo(new[] { "0:318-319" }));
    }

    [Test]
    public void SearchLines_SingleDotAtColumnZero_StaysSinglePixel()
    {
        // 旧版用 end.Y == 0 判断"线段延续到行尾"，第 0 列的单个墨点会被画成整行
        PixelImage image = Canvas((0, 0));

        List<List<DrawingLine>> rows = Scan(image, FullCanvas);

        Assert.That(rows[0].Select(Describe), Is.EqualTo(new[] { "0:0-0" }));
    }

    [Test]
    public void SearchLines_RunReachingRegionEnd_EndsAtRegionLastColumn()
    {
        PixelImage image = Canvas((0, 3), (0, 4), (0, 5));

        List<List<DrawingLine>> rows = Scan(image, FullCanvas with { ColCount = 5 });

        Assert.That(rows[0].Select(Describe), Is.EqualTo(new[] { "0:3-4" }));
    }

    [Test]
    public void SearchLines_Reverse_DrawsBrightPixels()
    {
        PixelImage image = Canvas((5, 10));

        List<List<DrawingLine>> rows = Scan(image, FullCanvas with { RowCount = 1, ColCount = 3 }, reverse: true);

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

        List<List<DrawingLine>> rows = Scan(image, FullCanvas);
        List<List<DrawingLine>> reversed = Scan(image, FullCanvas with { ColCount = 8 }, reverse: true);

        Assert.That(rows[0].Select(Describe), Is.EqualTo(new[] { "0:2-2", "0:6-6" }));
        Assert.That(reversed[0].Select(Describe), Is.EqualTo(new[] { "0:0-1", "0:3-5", "0:7-7" }));
    }

    [Test]
    public void Binarize_DefaultThreshold_MatchesLegacyBoundary()
    {
        // 50% = R+G+B 382.5：381 为墨点、383 为空白，与旧版一致；旧版恰好 382 时"保持当前状态"，现在按墨点处理
        byte[] rgba = White(320, 120);
        SetPixel(rgba, 0, 0, 127, 127, 127, 255);
        SetPixel(rgba, 0, 1, 127, 127, 128, 255);
        SetPixel(rgba, 0, 2, 127, 128, 128, 255);

        InkMask mask = InkMask.Binarize(new PixelImage(320, 120, rgba), new BinarizeOptions(50, false));

        Assert.That(new[] { mask[0, 0], mask[1, 0], mask[2, 0] }, Is.EqualTo(new[] { true, true, false }));
    }

    [TestCase(0, false, false)]
    [TestCase(30, false, false)]
    [TestCase(50, false, true)]
    [TestCase(100, false, true)]
    [TestCase(30, true, true)]
    [TestCase(50, true, false)]
    public void Binarize_ThresholdDecidesWhatIsInk(int percent, bool reverse, bool grayIsInk)
    {
        // 灰度 100（R+G+B = 300，约 39%）
        byte[] rgba = White(320, 120);
        SetPixel(rgba, 0, 0, 100, 100, 100, 255);

        InkMask mask = InkMask.Binarize(new PixelImage(320, 120, rgba), new BinarizeOptions(percent, reverse));

        Assert.That(mask[0, 0], Is.EqualTo(grayIsInk));
        Assert.That(mask[1, 0], Is.EqualTo(reverse && percent < 100), "纯白只在反转时可能成为墨点");
    }

    [Test]
    public void MaskPreview_IsBlackWhereInk()
    {
        InkMask mask = new(2, 1, [true, false]);

        PixelImage preview = mask.ToPreview();

        Assert.That(preview.GetPixel(0, 0).ToArgb(), Is.EqualTo(System.Drawing.Color.Black.ToArgb()));
        Assert.That(preview.GetPixel(1, 0).ToArgb(), Is.EqualTo(System.Drawing.Color.White.ToArgb()));
    }

    [Test]
    public async Task Preview_FollowsThresholdReverseAndShowOriginal()
    {
        byte[] rgba = White(320, 120);
        SetPixel(rgba, 0, 0, 100, 100, 100, 255);
        PixelImage image = new(320, 120, rgba);
        Fixture f = new(image);
        await f.Vm.LoadPictureFileAsync("x.png");
        Assert.That(f.Vm.Preview!.GetPixel(0, 0).R, Is.EqualTo(0), "默认阈值下灰度 100 是墨点");

        f.Vm.ThresholdPercent = 30;
        Assert.That(f.Vm.Mask![0, 0], Is.False);
        Assert.That(f.Vm.Preview!.GetPixel(0, 0).R, Is.EqualTo(255));

        f.Vm.Reverse = true;
        Assert.That(f.Vm.Mask![0, 0], Is.True);

        f.Vm.ShowOriginal = true;
        Assert.That(f.Vm.Preview, Is.SameAs(image));
        f.Vm.ShowOriginal = false;
        Assert.That(f.Vm.Preview, Is.Not.SameAs(image));
    }

    [Test]
    public async Task Threshold_ChangesWhatGetsDrawn()
    {
        byte[] rgba = White(320, 120);
        SetPixel(rgba, 0, 3, 100, 100, 100, 255);
        Fixture f = new(new PixelImage(320, 120, rgba));
        await f.Vm.LoadPictureFileAsync("x.png");
        f.Vm.DelayText = "0";
        f.Vm.DurationText = "1";
        f.Vm.ThresholdPercent = 30;

        await f.Vm.StartCommand.ExecuteAsync(null);
        await f.Vm.DrawTask!;

        Assert.That(f.Pad.Events, Does.Not.Contain("press A"), "阈值 30% 时灰度 100 不画");
    }

    [Test]
    public void SearchLines_ClampsRegionToCanvas()
    {
        PixelImage image = Canvas((119, 319), (100, 300));

        List<List<DrawingLine>> rows = Scan(image, FullCanvas with { StartRow = 100, StartCol = 300 });

        Assert.That(rows.SelectMany(r => r).Select(Describe), Is.EqualTo(new[] { "100:300-300", "119:319-319" }));
    }

    [Test]
    public void Estimate_SumsEveryWaitOfTheDrawSequence()
    {
        // 13 次方向点按 × (50 + 45)，外加每段线：两次移动后各 2 次、落笔后 1 次、抬笔后 1 次延迟，两段共 12 × 45
        List<List<DrawingLine>> rows = Scan(Canvas((0, 5), (0, 6), (2, 1)), FullCanvas);

        TimeSpan elapsed = SplatoonDrawing.Estimate(rows, 45, 50);

        Assert.That(elapsed, Is.EqualTo(TimeSpan.FromMilliseconds(13 * 95 + 12 * 45)));
    }

    [Test]
    public void Estimate_MatchesWaitsActuallyPerformedByDraw()
    {
        PixelImage image = Canvas((0, 5), (0, 6), (2, 1), (7, 300), (7, 302), (90, 0));
        RecordingPad pad = new();
        long waited = 0;

        List<List<DrawingLine>> rows = Scan(image, FullCanvas);
        TimeSpan estimated = SplatoonDrawing.Estimate(rows, 45, 50);
        new SplatoonDrawing(pad, 45, 50, (milliseconds, _) => waited += milliseconds).Draw(rows, CancellationToken.None);

        Assert.That(estimated, Is.EqualTo(TimeSpan.FromMilliseconds(waited + pad.ClickDurations)));
    }

    [Test]
    public void Draw_ReportsMonotonicProgressEndingAtEstimate()
    {
        PixelImage image = Canvas((0, 5), (0, 6), (2, 1), (7, 300), (7, 302), (90, 0));
        List<long> reports = [];

        List<List<DrawingLine>> rows = Scan(image, FullCanvas);
        TimeSpan estimated = SplatoonDrawing.Estimate(rows, 45, 50);
        new SplatoonDrawing(new RecordingPad(), 45, 50, (_, _) => { }, reports.Add).Draw(rows, CancellationToken.None);

        Assert.That(reports, Is.Ordered);
        Assert.That(reports.Last(), Is.EqualTo((long)estimated.TotalMilliseconds));
    }

    [Test]
    public void EstimateRemaining_UsesPlanFirstThenActualPace()
    {
        Assert.That(DrawingBoardViewModel.EstimateRemaining(0, 10_000, TimeSpan.Zero), Is.EqualTo(TimeSpan.FromSeconds(10)));
        Assert.That(DrawingBoardViewModel.EstimateRemaining(2_000, 10_000, TimeSpan.FromSeconds(3)), Is.EqualTo(TimeSpan.FromSeconds(8)));
        // 实际用时是计划的 2 倍：剩余 8 s 计划按 2 倍校正
        Assert.That(DrawingBoardViewModel.EstimateRemaining(5_000, 13_000, TimeSpan.FromSeconds(10)), Is.EqualTo(TimeSpan.FromSeconds(16)));
        Assert.That(DrawingBoardViewModel.EstimateRemaining(10_000, 10_000, TimeSpan.FromSeconds(30)), Is.EqualTo(TimeSpan.Zero));
    }

    [TestCase(65, "01:05")]
    [TestCase(3599, "59:59")]
    [TestCase(3725, "1:02:05")]
    [TestCase(36000, "10:00:00")]
    public void FormatClock_ShowsHoursOnlyWhenNeeded(int seconds, string expected)
    {
        Assert.That(DrawingBoardViewModel.FormatClock(TimeSpan.FromSeconds(seconds)), Is.EqualTo(expected));
    }

    [Test]
    public void Draw_EmitsLegacyKeySequence()
    {
        List<List<DrawingLine>> rows = Scan(Canvas((0, 5), (0, 6), (2, 1)), FullCanvas);
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
    public void Draw_DoesNotMutateRows_SoOneScanServesEstimateAndRepeatedDraws()
    {
        // 第 0 行先画，结束于第 302 列；第 7 行两段线需从右往左画，旧版会就地 Reverse 该行
        List<List<DrawingLine>> rows = Scan(Canvas((0, 302), (7, 5), (7, 300)), FullCanvas);
        string[] before = rows.SelectMany(r => r).Select(Describe).ToArray();
        RecordingPad first = new();
        RecordingPad second = new();

        TimeSpan estimated = SplatoonDrawing.Estimate(rows, 0, 0);
        new SplatoonDrawing(first, 0, 0).Draw(rows, CancellationToken.None);
        new SplatoonDrawing(second, 0, 0).Draw(rows, CancellationToken.None);

        Assert.That(rows.SelectMany(r => r).Select(Describe), Is.EqualTo(before));
        Assert.That(second.Events, Is.EqualTo(first.Events));
        Assert.That(first.Events, Is.EqualTo(Clicks(GamePadKey.RIGHT, 302)
            .Append("press A").Append("release A")
            .Concat(Clicks(GamePadKey.DOWN, 7)).Concat(Clicks(GamePadKey.LEFT, 2))
            .Append("press A").Append("release A")
            .Concat(Clicks(GamePadKey.LEFT, 295))
            .Append("press A").Append("release A")));
        Assert.That(estimated, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void Draw_CancelledBeforeStart_SendsNothing()
    {
        List<List<DrawingLine>> rows = Scan(Canvas((0, 5)), FullCanvas);
        RecordingPad pad = new();

        Assert.Throws<OperationCanceledException>(() => new SplatoonDrawing(pad, 0, 0).Draw(rows, new CancellationToken(true)));
        Assert.That(pad.Events, Is.Empty);
    }

    [Test]
    public void Draw_CancelledWhileKeyHeld_StopsPromptly()
    {
        List<List<DrawingLine>> rows = Scan(Canvas((0, 100), (0, 101)), FullCanvas);
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
        Assert.That(f.Vm.ProgressText, Does.StartWith("0% · 已用 00:00 · 剩余约 "));
        await f.Vm.StopCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(f.Vm.ProgressText, Does.StartWith("已停止 · 完成 "));

        Assert.That(f.Vm.DrawTask!.IsCompleted, Is.True);
        Assert.That(f.Vm.IsStartEnabled, Is.True);
        Assert.That(f.Dialogs.Messages, Is.Empty, "用户停止不应弹错误");
        int count = f.Pad.Events.Count;
        await Task.Delay(100);
        Assert.That(f.Pad.Events, Has.Count.EqualTo(count), "停止后不应再有按键");
        Assert.That(f.Pad.Events.Last(), Is.EqualTo(LeaseRelease));
    }

    [Test]
    public async Task Load_DecodesOnceAndPreviewsTheSamePixels()
    {
        PixelImage image = Canvas((0, 1));
        Fixture f = new(image);

        await f.Vm.LoadPictureFileAsync("x.png");

        Assert.That(f.Images.Decodes, Is.EqualTo(1));
        Assert.That(f.Vm.Mask, Is.Not.Null);
        f.Vm.ShowOriginal = true;
        Assert.That(f.Vm.Preview, Is.SameAs(image));
    }

    [TestCase(100, 50)]
    [TestCase(320, 100)]
    [TestCase(640, 480)]
    public async Task Load_AnySize_IsFittedToCanvas(int width, int height)
    {
        Fixture f = new(new PixelImage(width, height, White(width, height)));

        await f.Vm.LoadPictureFileAsync("x.png");

        Assert.That(f.Dialogs.Messages, Is.Empty);
        Assert.That(f.Images.Fits, Is.EqualTo(new[] { ImageFit.Contain }), "默认等比留白");
        Assert.That(f.Vm.Mask, Has.Property("Width").EqualTo(320).And.Property("Height").EqualTo(120));
        Assert.That(f.Vm.SourceSizeText, Is.EqualTo($"原图 {width}×{height}"));
    }

    [Test]
    public async Task FitMode_RefitsFromTheDecodedSourceWithoutReloading()
    {
        Fixture f = new(new PixelImage(640, 480, White(640, 480)));
        await f.Vm.LoadPictureFileAsync("x.png");

        f.Vm.FitModeIndex = 1;
        f.Vm.FitModeIndex = 2;

        Assert.That(f.Images.Fits, Is.EqualTo(new[] { ImageFit.Contain, ImageFit.Cover, ImageFit.Stretch }));
        Assert.That(f.Images.Decodes, Is.EqualTo(1));
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

        Assert.That(f.Vm.Preview, Is.Not.Null);
        Assert.That(f.Pad.Events, Does.Contain("press A"));
        Assert.That(f.Pad.Events.Last(), Is.EqualTo(LeaseRelease));
        Assert.That(f.Vm.IsStartEnabled, Is.True);
        Assert.That(f.Vm.ProgressValue, Is.EqualTo(100));
        Assert.That(f.Vm.ProgressText, Does.StartWith("已完成 · 用时 "));
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
        Assert.That(f.Vm.ProgressText, Is.EqualTo("已中断 · 完成 0%"));
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
        Assert.That(f.Vm.ProgressText, Does.StartWith("已中断 · 完成 "));
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

    private static Dictionary<string, string> Locale(string name)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XDocument doc = XDocument.Load(Path.Combine(RepoRoot(), "src", "EasyCon2.Avalonia", "Resources", "Locales", name + ".axaml"));
        return doc.Root!.Elements(x + "String").ToDictionary(e => (string)e.Attribute(x + "Key")!, e => e.Value);
    }

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "EasyCon2.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("找不到仓库根目录（EasyCon2.slnx）");
    }

    private static List<List<DrawingLine>> Scan(PixelImage image, DrawingBoardSettings settings, bool reverse = false)
        => SplatoonDrawing.SearchLines(InkMask.Binarize(image, new BinarizeOptions(50, reverse)), settings);

    private static string Describe(DrawingLine line) => $"{line.Row}:{line.StartCol}-{line.EndCol}";

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
            Images = new FakeImages(image);
            Vm = new DrawingBoardViewModel(Runner, Dialogs, Images, SynchronousUiDispatcher.Instance, new NullLog());
        }

        public FakeImages Images { get; }
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
        public int Decodes { get; private set; }
        public byte[]? Crop(byte[] sourceBytes, int x, int y, int width, int height) => null;

        public byte[]? LoadAsPng(string filePath)
        {
            Decodes++;
            return [1];
        }

        public PixelImage? LoadPixels(string filePath)
        {
            Decodes++;
            return image;
        }

        public List<ImageFit> Fits { get; } = [];

        /// <summary>记录适配方式；尺寸已一致时原样返回（与真实实现相同），否则给一张同尺寸白图。</summary>
        public PixelImage FitTo(PixelImage source, int width, int height, ImageFit fit)
        {
            Fits.Add(fit);
            return source.Width == width && source.Height == height ? source : new PixelImage(width, height, White(width, height));
        }
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