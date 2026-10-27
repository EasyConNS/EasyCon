using EasyCon2.Avalonia.DrawingBoard;
using EasyCon2.Avalonia.Models;
using System.Text.RegularExpressions;
using static EasyCon2.Avalonia.Core.Tests.DrawingBoardTestKit;

namespace EasyCon2.Avalonia.Core.Tests;

/// <summary>墨点掩码：阈值二值化、各抖动算法、反转与预览。</summary>
[TestFixture]
[NonParallelizable]
public class InkMaskTests : DrawingBoardTestBase
{
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

    private static readonly DitherMode[] DitherModes = Enum.GetValues<DitherMode>().Where(m => m != DitherMode.None).ToArray();

    [TestCaseSource(nameof(DitherModes))]
    public void Dither_TurnsMidGrayIntoAboutHalfDots(DitherMode mode)
    {
        PixelImage gray = Filled(128);

        double plain = InkFraction(InkMask.Binarize(gray, new BinarizeOptions(50, false)));
        double dithered = InkFraction(InkMask.Binarize(gray, new BinarizeOptions(50, false, mode)));

        Assert.That(plain, Is.Zero, "不抖动时灰度 128 略亮于分界，整片留空");
        Assert.That(dithered, Is.InRange(0.4, 0.6));
    }

    [TestCaseSource(nameof(DitherModes))]
    public void Dither_LeavesPureBlackAndWhiteUnchanged(DitherMode mode)
    {
        PixelImage image = Canvas((5, 5), (5, 6), (60, 200), (119, 319));

        InkMask plain = InkMask.Binarize(image, new BinarizeOptions(50, false));
        InkMask dithered = InkMask.Binarize(image, new BinarizeOptions(50, false, mode));
        InkMask reversed = InkMask.Binarize(image, new BinarizeOptions(50, true, mode));

        Assert.That(Cells(dithered), Is.EqualTo(Cells(plain)));
        Assert.That(Cells(reversed), Is.EqualTo(Cells(plain).Select(ink => !ink)));
    }

    [TestCaseSource(nameof(DitherModes))]
    public void Dither_DotDensityFollowsBrightness(DitherMode mode)
    {
        byte[] rgba = White(320, 120);
        for (int row = 0; row < 120; row++)
            for (int col = 0; col < 320; col++)
            {
                byte value = (byte)(255 * col / 319);
                SetPixel(rgba, row, col, value, value, value, 255);
            }

        InkMask mask = InkMask.Binarize(new PixelImage(320, 120, rgba), new BinarizeOptions(50, false, mode));

        Assert.That(InkFraction(mask, 0, 80), Is.GreaterThan(0.75));
        Assert.That(InkFraction(mask, 240, 320), Is.LessThan(0.25));
    }

    [Test]
    public void Atkinson_UsesFewerDotsInLightAreasThanFloydSteinberg()
    {
        PixelImage light = Filled(200);

        double floyd = InkFraction(InkMask.Binarize(light, new BinarizeOptions(50, false, DitherMode.FloydSteinberg)));
        double atkinson = InkFraction(InkMask.Binarize(light, new BinarizeOptions(50, false, DitherMode.Atkinson)));

        Assert.That(atkinson, Is.LessThan(floyd * 0.8));
    }

    [Test]
    public void Serpentine_ChangesThePatternButNotTheDensity()
    {
        PixelImage gray = Filled(90);

        InkMask raster = InkMask.Binarize(gray, new BinarizeOptions(50, false, DitherMode.FloydSteinberg));
        InkMask serpentine = InkMask.Binarize(gray, new BinarizeOptions(50, false, DitherMode.FloydSteinbergSerpentine));

        Assert.That(Cells(serpentine), Is.Not.EqualTo(Cells(raster)));
        Assert.That(InkFraction(serpentine), Is.EqualTo(InkFraction(raster)).Within(0.02));
    }

    [Test]
    public void Bayer_RepeatsOnAnEightByEightGrid()
    {
        InkMask mask = InkMask.Binarize(Filled(100), new BinarizeOptions(50, false, DitherMode.Bayer));

        for (int row = 0; row < 112; row++)
            for (int col = 0; col < 312; col++)
                Assert.That(mask[col, row], Is.EqualTo(mask[col + 8, row]).And.EqualTo(mask[col, row + 8]), $"({col},{row})");
    }

    [Test]
    public void DitherDropdown_ListsEveryModeInEnumOrder()
    {
        string axaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "EasyCon2.Avalonia", "DrawingBoard", "DrawingBoardWindow.axaml"));
        string[] items = Regex.Matches(axaml, @"Text\.DrawingBoard\.Dither(\w+)}""/>").Select(m => m.Groups[1].Value).ToArray();

        Assert.That(items, Is.EqualTo(Enum.GetNames<DitherMode>()));
    }

    [Test]
    public void Inverted_SwapsInkAndBlank()
    {
        InkMask mask = new(3, 1, [true, false, true]);

        InkMask inverted = mask.Inverted();

        Assert.That(Cells(inverted), Is.EqualTo(new[] { false, true, false }));
        Assert.That(Cells(mask), Is.EqualTo(new[] { true, false, true }), "原掩码不变");
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
    public void ToneAdjust_MapsBrightness()
    {
        Assert.That(ToneAdjust.Identity.Apply(0.3), Is.EqualTo(0.3).Within(1e-9));
        Assert.That(new ToneAdjust(50, 0, 1).Apply(0.3), Is.EqualTo(0.8).Within(1e-9), "亮度 +50 即加半档");
        Assert.That(new ToneAdjust(-100, 0, 1).Apply(0.9), Is.Zero, "结果截到 0～1");
        Assert.That(new ToneAdjust(0, 50, 1).Apply(0.6), Is.EqualTo(0.7).Within(1e-9), "对比度 +50：离中灰的距离放大 2 倍");
        Assert.That(new ToneAdjust(0, -50, 1).Apply(0.9), Is.EqualTo(0.7).Within(1e-9), "对比度 -50：距离减半");
        Assert.That(new ToneAdjust(0, 0, 2).Apply(0.25), Is.EqualTo(0.5).Within(1e-9), "伽马 2：开平方提亮");
        Assert.That(new ToneAdjust(0, 0, 2).Apply(1), Is.EqualTo(1), "纯白不变");
    }

    [Test]
    public void Binarize_ToneShiftsWhatIsInk()
    {
        byte[] rgba = White(320, 120);
        SetPixel(rgba, 0, 0, 100, 100, 100, 255);   // 约 39% 亮度
        PixelImage image = new(320, 120, rgba);

        Assert.That(InkMask.Binarize(image, new BinarizeOptions(50, false))[0, 0], Is.True);
        Assert.That(InkMask.Binarize(image, new BinarizeOptions(50, false, Tone: new ToneAdjust(20, 0, 1)))[0, 0], Is.False, "提亮后不再算墨点");
        Assert.That(InkMask.Binarize(image, new BinarizeOptions(50, false, Tone: new ToneAdjust(0, 0, 2)))[0, 0], Is.False, "伽马提亮暗部");
        Assert.That(InkMask.Binarize(image, new BinarizeOptions(50, false, Tone: ToneAdjust.Identity))[0, 0], Is.True);
    }

    [Test]
    public void RemoveSpecks_RemovesSmallEightConnectedBlobsOnly()
    {
        // 单点 (0,0)；斜向相连的两点 (5,5)(6,6)；三点横线 (10,20..22)
        InkMask mask = InkMask.Binarize(Canvas((0, 0), (5, 5), (6, 6), (10, 20), (10, 21), (10, 22)), new BinarizeOptions(50, false));

        InkMask one = mask.RemoveSpecks(1);
        InkMask two = mask.RemoveSpecks(2);

        Assert.That((one[0, 0], one[5, 5], one[6, 6], one[20, 10]), Is.EqualTo((false, true, true, true)), "斜向相连算一块");
        Assert.That((two[5, 5], two[6, 6], two[20, 10]), Is.EqualTo((false, false, true)));
        Assert.That(mask.RemoveSpecks(0), Is.SameAs(mask));
        Assert.That(mask[0, 0], Is.True, "不修改原掩码");
    }

    [Test]
    public void Outline_HollowsSolidAreasButKeepsLinesAndDots()
    {
        List<(int, int)> square = [];
        for (int r = 10; r < 15; r++)
            for (int c = 10; c < 15; c++)
                square.Add((r, c));
        InkMask mask = InkMask.Binarize(Canvas([.. square, (30, 50), (40, 60), (40, 61), (40, 62), (0, 0), (0, 1), (1, 0), (1, 1)]),
            new BinarizeOptions(50, false));

        InkMask outline = mask.Outline();

        Assert.That(Cells(outline).Count(ink => ink), Is.EqualTo(16 + 1 + 3 + 3), "5×5 方块剩外圈 16 点，单点、线段不变，角上 2×2 小块去掉贴角的一点");
        Assert.That(outline[12, 12], Is.False, "方块内部被挖空");
        Assert.That(outline[10, 12], Is.True);
        Assert.That(outline[0, 0], Is.False, "画布边缘不算轮廓");
    }

    [Test]
    public void Outline_WithReverse_DoesNotFrameTheCanvas()
    {
        // 白底黑圆：反转后背景成了墨点，轮廓只应是圆周外侧一圈，不能把整圈画布边框也算进来
        List<(int, int)> disc = [];
        for (int r = 0; r < 120; r++)
            for (int c = 0; c < 320; c++)
                if ((c - 160) * (c - 160) + (r - 60) * (r - 60) < 30 * 30)
                    disc.Add((r, c));
        PixelImage image = Canvas([.. disc]);

        InkMask normal = InkMask.Binarize(image, new BinarizeOptions(50, false)).Outline();
        InkMask reversed = InkMask.Binarize(image, new BinarizeOptions(50, true)).Outline();

        int Border(InkMask mask) =>
            Enumerable.Range(0, 320).Count(c => mask[c, 0] || mask[c, 119]) + Enumerable.Range(1, 118).Count(r => mask[0, r] || mask[319, r]);
        Assert.That(Border(reversed), Is.Zero);
        Assert.That(Cells(reversed).Count(ink => ink), Is.EqualTo(Cells(normal).Count(ink => ink)).Within(10),
            "反转后是圆周外侧一圈，与不反转时的内侧一圈点数相近");
        Assert.That(reversed[160, 60], Is.False, "圆内（反转后的空白）不画");
        Assert.That(reversed[5, 5], Is.False, "背景内部（反转后的实心区域）被挖空");
    }

    [Test]
    public void Outline_OfAFullyInkedCanvas_IsEmpty()
    {
        InkMask full = new(320, 120, Enumerable.Repeat(true, 320 * 120).ToArray());

        Assert.That(Cells(full.Outline()).Any(ink => ink), Is.False, "整张都是墨点时没有边，不画边框");
    }
}