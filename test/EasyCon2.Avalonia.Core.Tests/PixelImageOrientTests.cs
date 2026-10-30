using EasyCon2.Avalonia.DrawingBoard;
using EasyCon2.Avalonia.Models;
using static EasyCon2.Avalonia.Core.Tests.DrawingBoardTestKit;

namespace EasyCon2.Avalonia.Core.Tests;

/// <summary>原图的 90° 旋转与翻转（只搬动像素）。</summary>
[TestFixture]
[NonParallelizable]
public class PixelImageOrientTests : DrawingBoardTestBase
{
    // 3×2 原图，像素按编号：
    //   0 1 2
    //   3 4 5
    [TestCase(0, false, false, 3, "0 1 2|3 4 5")]
    [TestCase(1, false, false, 2, "3 0|4 1|5 2")]
    [TestCase(2, false, false, 3, "5 4 3|2 1 0")]
    [TestCase(3, false, false, 2, "2 5|1 4|0 3")]
    [TestCase(0, true, false, 3, "2 1 0|5 4 3")]
    [TestCase(0, false, true, 3, "3 4 5|0 1 2")]
    [TestCase(0, true, true, 3, "5 4 3|2 1 0")]
    [TestCase(1, true, false, 2, "0 3|1 4|2 5")]
    [TestCase(1, false, true, 2, "5 2|4 1|3 0")]
    [TestCase(-1, false, false, 2, "2 5|1 4|0 3")]
    public void Orient_RotatesClockwiseThenFlips(int quarterTurns, bool flipHorizontal, bool flipVertical, int width, string expected)
    {
        byte[] rgba = new byte[3 * 2 * 4];
        for (int i = 0; i < 6; i++)
        {
            rgba[i * 4] = (byte)i;
            rgba[i * 4 + 3] = 255;
        }

        PixelImage oriented = new PixelImage(3, 2, rgba).Orient(quarterTurns, flipHorizontal, flipVertical);

        Assert.That(oriented.Width, Is.EqualTo(width));
        string actual = string.Join("|", Enumerable.Range(0, oriented.Height)
            .Select(y => string.Join(" ", Enumerable.Range(0, oriented.Width).Select(x => oriented.GetPixel(x, y).R))));
        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void Orient_WithoutChange_ReturnsTheSameImage()
    {
        PixelImage image = Canvas((0, 0));

        Assert.That(image.Orient(0, false, false), Is.SameAs(image));
        Assert.That(image.Orient(4, false, false), Is.SameAs(image));
    }
}