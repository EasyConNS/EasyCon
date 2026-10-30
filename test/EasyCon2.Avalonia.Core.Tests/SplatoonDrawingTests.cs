using EasyCon2.Avalonia.DrawingBoard;
using EasyCon2.Avalonia.Models;
using EasyScript;
using static EasyCon2.Avalonia.Core.Tests.DrawingBoardTestKit;

namespace EasyCon2.Avalonia.Core.Tests;

/// <summary>喷射战士画板的扫描、估算与发键序列（对齐 1.6.3 WinForms 版的移动路径）。</summary>
[TestFixture]
[NonParallelizable]
public class SplatoonDrawingTests : DrawingBoardTestBase
{
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
    public void SearchLines_ClampsRegionToCanvas()
    {
        PixelImage image = Canvas((119, 319), (100, 300));

        List<List<DrawingLine>> rows = Scan(image, FullCanvas with { StartRow = 100, StartCol = 300 });

        Assert.That(rows.SelectMany(r => r).Select(Describe), Is.EqualTo(new[] { "100:300-300", "119:319-319" }));
    }

    [Test]
    public void Estimate_SumsEveryWaitOfTheDrawSequence()
    {
        // 归位（摇杆推一下 + 推住，各 1 次延迟）；13 次方向点按 × (50 + 45)；每段线：两次移动后各 2 次、落笔后 1 次、抬笔后 1 次延迟，两段共 12 × 45
        List<List<DrawingLine>> rows = Scan(Canvas((0, 5), (0, 6), (2, 1)), FullCanvas);

        TimeSpan elapsed = SplatoonDrawing.Estimate(rows, 45, 50);

        Assert.That(elapsed, Is.EqualTo(TimeSpan.FromMilliseconds(HomeMs(45) + 13 * 95 + 12 * 45)));
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
        new SplatoonDrawing(new RecordingPad(), 45, 50, (_, _) => { }, (done, _) => reports.Add(done.PlannedMs)).Draw(rows, CancellationToken.None);

        Assert.That(reports, Is.Ordered);
        Assert.That(reports.Last(), Is.EqualTo((long)estimated.TotalMilliseconds));
    }

    [Test]
    public void Draw_EmitsLegacyKeySequence()
    {
        List<List<DrawingLine>> rows = Scan(Canvas((0, 5), (0, 6), (2, 1)), FullCanvas);
        RecordingPad pad = new();

        new SplatoonDrawing(pad, 0, 0).Draw(rows, CancellationToken.None);

        List<string> expected = [.. HomeKeys];
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
        Assert.That(first.Events, Is.EqualTo(HomeKeys.Concat(Clicks(GamePadKey.RIGHT, 302))
            .Append("press A").Append("release A")
            .Concat(Clicks(GamePadKey.DOWN, 7)).Concat(Clicks(GamePadKey.LEFT, 2))
            .Append("press A").Append("release A")
            .Concat(Clicks(GamePadKey.LEFT, 295))
            .Append("press A").Append("release A")));
        Assert.That(estimated, Is.EqualTo(TimeSpan.FromMilliseconds(HomeMs(0))));
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
        Assert.That(pad.Events, Is.EqualTo(new[] { Home }));
    }

    [Test]
    public void Draw_PausedWhilePenDown_LiftsAndRepressesThePen()
    {
        List<List<DrawingLine>> rows = Scan(Canvas((0, 5), (0, 6)), FullCanvas);
        RecordingPad pad = new();
        DrawPause pause = new();
        bool paused = false;
        pad.OnKey = () =>
        {
            if (paused || pad.Events.Last() != "press A")
                return;
            paused = true;
            pause.Pause();
            _ = Task.Run(async () =>
            {
                await Task.Delay(100);
                pause.Resume();
            });
        };

        new SplatoonDrawing(pad, 0, 0, pause: pause).Draw(rows, CancellationToken.None);

        // 落笔后暂停：下一次按键前先松开画笔，继续后原位重新按下再走
        List<string> expected = [.. HomeKeys, .. Clicks(GamePadKey.RIGHT, 5), "press A", "release A", "press A", "click RIGHT", "release A"];
        Assert.That(pad.Events, Is.EqualTo(expected));
    }

    [Test]
    public void Draw_PausedDoesNotChangePlannedProgress()
    {
        List<List<DrawingLine>> rows = Scan(Canvas((0, 5), (0, 6), (2, 1)), FullCanvas);
        TimeSpan estimated = SplatoonDrawing.Estimate(rows, 45, 50);
        DrawPause pause = new();
        pause.Pause();
        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            pause.Resume();
        });
        long last = 0;

        new SplatoonDrawing(new RecordingPad(), 45, 50, (_, _) => { }, (done, _) => last = done.PlannedMs, pause: pause)
            .Draw(rows, CancellationToken.None);

        Assert.That(last, Is.EqualTo((long)estimated.TotalMilliseconds));
    }

    [Test]
    public void Draw_CancelledWhilePaused_StopsPromptly()
    {
        List<List<DrawingLine>> rows = Scan(Canvas((0, 5)), FullCanvas);
        DrawPause pause = new();
        pause.Pause();
        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(100));
        System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();

        Assert.Throws<OperationCanceledException>(() => new SplatoonDrawing(new RecordingPad(), 0, 0, pause: pause).Draw(rows, cts.Token));
        Assert.That(elapsed.ElapsedMilliseconds, Is.LessThan(2_000));
    }

    [Test]
    public void Draw_ReportsEveryStepWithThePenState()
    {
        List<List<DrawingLine>> rows = Scan(Canvas((0, 2), (0, 3), (1, 1)), FullCanvas);
        List<(int Row, int Col, bool PenDown)> steps = [];

        new SplatoonDrawing(new RecordingPad(), 0, 0, step: (row, col, pen) => steps.Add((row, col, pen))).Draw(rows, CancellationToken.None);

        Assert.That(steps, Is.EqualTo(new[]
        {
            (0, 0, false),                          // 归位到左上角
            (0, 1, false), (0, 2, false),           // 抬笔右移到第 2 列
            (0, 2, true), (0, 3, true), (0, 3, false), // 落笔画到第 3 列，抬笔
            (1, 3, false), (1, 2, false), (1, 1, false), // 下移一行，左移到第 1 列
            (1, 1, true), (1, 1, false),            // 单点
        }));
    }

    [TestCase(0)]
    [TestCase(7)]
    [TestCase(42)]
    public void Draw_ReplayedOnCanvas_PaintsExactlyTheInk(int seed)
    {
        // 随机墨点 + 首末列整列，按发出的键在 320×120 画布上回放（光标在边缘停住），按住 A 经过的格子即被画上
        Random random = new(seed);
        List<(int Row, int Col)> dark = [];
        for (int row = 0; row < 120; row++)
            for (int col = 0; col < 320; col++)
                if (col == 0 || col == 319 || random.Next(3) == 0)
                    dark.Add((row, col));
        PixelImage image = Canvas([.. dark]);
        RecordingPad pad = new();

        new SplatoonDrawing(pad, 0, 0).Draw(Scan(image, FullCanvas), CancellationToken.None);

        HashSet<(int Row, int Col)> painted = [];
        int r = 0, c = 0;
        bool pen = false;
        foreach (string e in pad.Events)
        {
            switch (e)
            {
                case "press A": pen = true; break;
                case "release A": pen = false; break;
                case "click RIGHT": c = Math.Min(319, c + 1); break;
                case "click LEFT": c = Math.Max(0, c - 1); break;
                case "click DOWN": r = Math.Min(119, r + 1); break;
                case "click TOP": r = Math.Max(0, r - 1); break;
                case Home: r = 0; c = 0; break;
            }
            if (pen)
                painted.Add((r, c));
        }
        Assert.That(painted, Is.EquivalentTo(dark));
    }

    [Test]
    public void EstimateCost_CountsEachKindOfOperation()
    {
        List<List<DrawingLine>> rows = Scan(Canvas((0, 5), (0, 6), (2, 1)), FullCanvas);

        DrawingCost cost = SplatoonDrawing.EstimateCost(rows, 45, 50);

        // 归位 2 次 + 13 次方向点按；两段线各落笔、抬笔一次；归位后 2 次 + 每次点按后 1 次 + 每段线 6 次延迟
        Assert.That(cost, Is.EqualTo(new DrawingCost(HomeMs(45) + 13 * 95 + 12 * 45, 15, 4, 2 + 13 + 12)));
    }

    [Test]
    public void Draw_WithoutHome_SkipsHomingAndItsCost()
    {
        List<List<DrawingLine>> rows = Scan(Canvas((0, 5)), FullCanvas);
        RecordingPad pad = new();

        new SplatoonDrawing(pad, 0, 0, home: false).Draw(rows, CancellationToken.None);

        Assert.That(pad.Events, Is.EqualTo(Clicks(GamePadKey.RIGHT, 5).Append("press A").Append("release A")));
        Assert.That(SplatoonDrawing.Estimate(rows, 45, 50) - SplatoonDrawing.Estimate(rows, 45, 50, home: false),
            Is.EqualTo(TimeSpan.FromMilliseconds(HomeMs(45))));
    }

    [Test]
    public void Draw_MeasuresTimeBeyondThePlanPerOperation()
    {
        // 每次点按与落笔在手柄上多花约 5 ms（模拟设备通信往返；抬笔不花），等待则不花时间
        List<List<DrawingLine>> rows = Scan(Canvas((0, 5), (0, 6), (2, 1)), FullCanvas);
        RecordingPad pad = new() { OnKey = () => Thread.Sleep(5) };
        DrawingOverhead overhead = default;

        // 不归位：假手柄不真的推住 4 秒，归位那一下会显得比计划快得多
        SplatoonDrawing drawing = new(pad, 0, 0, (_, _) => { }, (_, measured) => overhead = measured, home: false);
        drawing.Draw(rows, CancellationToken.None);

        Assert.That(overhead.ClickMs, Is.GreaterThanOrEqualTo(4));
        Assert.That(overhead.ToggleMs, Is.GreaterThanOrEqualTo(2), "落笔、抬笔各半");
        Assert.That(drawing.Overhead, Is.EqualTo(overhead));
    }
}