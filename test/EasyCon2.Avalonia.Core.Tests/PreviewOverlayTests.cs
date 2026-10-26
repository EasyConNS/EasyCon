using EasyCon2.Avalonia.DrawingBoard;
using EasyCon2.Avalonia.Models;
using static EasyCon2.Avalonia.Core.Tests.DrawingBoardTestKit;

namespace EasyCon2.Avalonia.Core.Tests;

/// <summary>预览叠加：绘制中的游戏画面估计、光标标记与框选区域。</summary>
[TestFixture]
public class PreviewOverlayTests
{
    private static (byte R, byte G, byte B) Rgb(PixelImage image, int col, int row)
    {
        System.Drawing.Color c = image.GetPixel(col, row);
        return (c.R, c.G, c.B);
    }

    [Test]
    public void RenderProgress_PenMode_ShowsDrawnBlackPendingGrayRestWhite()
    {
        InkMask strokes = new(3, 1, [true, true, false]);
        bool[] done = [true, false, false];

        PixelImage image = PreviewOverlay.RenderProgress(strokes, done, eraser: false, cursor: null);

        Assert.That(Rgb(image, 0, 0), Is.EqualTo(((byte)0, (byte)0, (byte)0)));
        Assert.That(Rgb(image, 1, 0), Is.EqualTo(PreviewOverlay.PendingInk));
        Assert.That(Rgb(image, 2, 0), Is.EqualTo(((byte)255, (byte)255, (byte)255)));
    }

    [Test]
    public void RenderProgress_EraserMode_ShowsErasedWhitePendingGrayRestBlack()
    {
        InkMask strokes = new(3, 1, [true, true, false]);
        bool[] done = [true, false, false];

        PixelImage image = PreviewOverlay.RenderProgress(strokes, done, eraser: true, cursor: null);

        Assert.That(Rgb(image, 0, 0), Is.EqualTo(((byte)255, (byte)255, (byte)255)));
        Assert.That(Rgb(image, 1, 0), Is.EqualTo(PreviewOverlay.PendingErase));
        Assert.That(Rgb(image, 2, 0), Is.EqualTo(((byte)0, (byte)0, (byte)0)));
    }

    [Test]
    public void RenderProgress_MarksTheCursorWithARing_ClippedAtEdges()
    {
        InkMask strokes = new(320, 120, new bool[320 * 120]);

        PixelImage image = PreviewOverlay.RenderProgress(strokes, new bool[320 * 120], eraser: false, cursor: (60, 100));
        PixelImage corner = PreviewOverlay.RenderProgress(strokes, new bool[320 * 120], eraser: false, cursor: (0, 0));

        Assert.That(Rgb(image, 100, 60), Is.EqualTo(PreviewOverlay.Cursor), "光标点");
        Assert.That(Rgb(image, 98, 58), Is.EqualTo(PreviewOverlay.Cursor), "外圈");
        Assert.That(Rgb(image, 99, 59), Is.EqualTo(((byte)255, (byte)255, (byte)255)), "外圈与光标之间留空");
        Assert.That(Rgb(corner, 2, 2), Is.EqualTo(PreviewOverlay.Cursor), "贴边时只画画布内的部分");
    }

    [Test]
    public void WithRegionOutline_TintsOnlyTheBorder()
    {
        PixelImage white = new(320, 120, White(320, 120));

        PixelImage framed = PreviewOverlay.WithRegionOutline(white, 10, 5, 20, 10);

        Assert.That(Rgb(framed, 10, 5), Is.Not.EqualTo(((byte)255, (byte)255, (byte)255)), "左上角");
        Assert.That(Rgb(framed, 29, 14), Is.Not.EqualTo(((byte)255, (byte)255, (byte)255)), "右下角");
        Assert.That(Rgb(framed, 20, 10), Is.EqualTo(((byte)255, (byte)255, (byte)255)), "框内不变");
        Assert.That(Rgb(framed, 30, 15), Is.EqualTo(((byte)255, (byte)255, (byte)255)), "框外不变");
        Assert.That(Rgb(white, 10, 5), Is.EqualTo(((byte)255, (byte)255, (byte)255)), "不修改原图");
    }
}