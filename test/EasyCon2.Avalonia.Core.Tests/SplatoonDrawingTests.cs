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
}