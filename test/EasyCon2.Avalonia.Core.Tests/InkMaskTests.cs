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
}