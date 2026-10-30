using EasyCon2.Avalonia.Models;
using EasyCon2.Avalonia.Services;

namespace EasyCon2.Avalonia.UiTests;

/// <summary>SkiaImageProcessor.FitTo：三种适配方式，以及等比裁切的任意角度、缩放与取景位置。</summary>
[TestFixture]
public class SkiaImageFitTests
{
    /// <summary>左半黑、右半白的不透明图。</summary>
    internal static PixelImage HalfBlack(int width, int height)
    {
        byte[] rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                byte v = x < width / 2 ? (byte)0 : (byte)255;
                rgba[i] = rgba[i + 1] = rgba[i + 2] = v;
                rgba[i + 3] = 255;
            }
        return new PixelImage(width, height, rgba);
    }

    /// <summary>只有中间 150～169 列为黑的不透明 320×120 图。</summary>
    internal static PixelImage CenterStripe()
    {
        byte[] rgba = new byte[320 * 120 * 4];
        for (int y = 0; y < 120; y++)
            for (int x = 0; x < 320; x++)
            {
                int i = (y * 320 + x) * 4;
                byte v = x is >= 150 and < 170 ? (byte)0 : (byte)255;
                rgba[i] = rgba[i + 1] = rgba[i + 2] = v;
                rgba[i + 3] = 255;
            }
        return new PixelImage(320, 120, rgba);
    }

    [Test]
    public void FitTo_SameSize_ReturnsSourceUntouched()
    {
        PixelImage source = HalfBlack(320, 120);

        Assert.That(new SkiaImageProcessor().FitTo(source, 320, 120, ImageFit.Contain), Is.SameAs(source));
    }

    [Test]
    public void FitTo_Contain_LetterboxesWithTransparency()
    {
        // 160×120 等比放进 320×120：居中占 80～239 列，两侧透明
        PixelImage fitted = new SkiaImageProcessor().FitTo(HalfBlack(160, 120), 320, 120, ImageFit.Contain);

        Assert.That(fitted.Width, Is.EqualTo(320));
        Assert.That(fitted.GetPixel(20, 60).A, Is.EqualTo(0), "左侧留白");
        Assert.That(fitted.GetPixel(300, 60).A, Is.EqualTo(0), "右侧留白");
        Assert.That(fitted.GetPixel(100, 60).R, Is.LessThan(20), "原图左半（黑）");
        Assert.That(fitted.GetPixel(220, 60).R, Is.GreaterThan(235), "原图右半（白）");
    }

    [Test]
    public void FitTo_Cover_CropsTheCenter()
    {
        // 640×120 铺满 320×120：缩放比例 1，取中间 160～479 列，即原图左半的后段 + 右半的前段
        PixelImage fitted = new SkiaImageProcessor().FitTo(HalfBlack(640, 120), 320, 120, ImageFit.Cover);

        Assert.That(fitted.GetPixel(20, 60).R, Is.LessThan(20));
        Assert.That(fitted.GetPixel(300, 60).R, Is.GreaterThan(235));
        Assert.That(fitted.GetPixel(20, 60).A, Is.EqualTo(255), "铺满后没有留白");
    }

    [Test]
    public void FitTo_Stretch_FillsWholeCanvas()
    {
        PixelImage fitted = new SkiaImageProcessor().FitTo(HalfBlack(100, 100), 320, 120, ImageFit.Stretch);

        Assert.That(fitted.GetPixel(5, 5).R, Is.LessThan(20));
        Assert.That(fitted.GetPixel(150, 110).R, Is.LessThan(20), "左半拉伸到 0～159 列");
        Assert.That(fitted.GetPixel(170, 110).R, Is.GreaterThan(235));
        Assert.That(fitted.GetPixel(315, 5).A, Is.EqualTo(255));
    }

    [Test]
    public void FitTo_CoverWithAdjustments_DoesNotTakeTheSameSizeShortcut()
    {
        PixelImage source = CenterStripe();
        SkiaImageProcessor processor = new();

        Assert.That(processor.FitTo(source, 320, 120, ImageFit.Cover, CoverTransform.Identity), Is.SameAs(source));
        Assert.That(processor.FitTo(source, 320, 120, ImageFit.Cover, new CoverTransform(10, 1, 0, 0)), Is.Not.SameAs(source));
        Assert.That(processor.FitTo(source, 320, 120, ImageFit.Contain, new CoverTransform(10, 1, 0, 0)), Is.SameAs(source),
            "取景调整只对等比裁切生效");
    }

    [Test]
    public void FitTo_CoverPan_SelectsWhichPartIsKept()
    {
        // 640×120 左半黑：贴左只看到黑，贴右只看到白
        SkiaImageProcessor processor = new();

        PixelImage left = processor.FitTo(HalfBlack(640, 120), 320, 120, ImageFit.Cover, new CoverTransform(0, 1, -1, 0));
        PixelImage right = processor.FitTo(HalfBlack(640, 120), 320, 120, ImageFit.Cover, new CoverTransform(0, 1, 1, 0));

        Assert.That(left.GetPixel(300, 60).R, Is.LessThan(20));
        Assert.That(right.GetPixel(20, 60).R, Is.GreaterThan(235));
    }

    [Test]
    public void FitTo_CoverZoom_EnlargesAroundTheCenter()
    {
        PixelImage zoomed = new SkiaImageProcessor().FitTo(CenterStripe(), 320, 120, ImageFit.Cover, new CoverTransform(0, 2, 0, 0));

        // 20 列宽的黑条放大 2 倍后占 140～179 列
        Assert.That(zoomed.GetPixel(145, 60).R, Is.LessThan(20));
        Assert.That(zoomed.GetPixel(175, 60).R, Is.LessThan(20));
        Assert.That(zoomed.GetPixel(135, 60).R, Is.GreaterThan(235));
    }

    [Test]
    public void FitTo_CoverAngle90_TurnsTheLeftHalfToTheTop()
    {
        PixelImage turned = new SkiaImageProcessor().FitTo(HalfBlack(320, 120), 320, 120, ImageFit.Cover, new CoverTransform(90, 1, 0, 0));

        Assert.That(turned.GetPixel(160, 10).R, Is.LessThan(20), "顺时针 90°：原图左侧到上方");
        Assert.That(turned.GetPixel(160, 110).R, Is.GreaterThan(235));
    }

    [TestCase(17)]
    [TestCase(45)]
    [TestCase(-120)]
    public void FitTo_CoverAnyAngle_StillCoversTheWholeCanvasWhenCentered(double angle)
    {
        PixelImage turned = new SkiaImageProcessor().FitTo(HalfBlack(400, 300), 320, 120, ImageFit.Cover, new CoverTransform(angle, 1, 0, 0));

        foreach ((int x, int y) in new[] { (0, 0), (319, 0), (0, 119), (319, 119), (160, 60) })
            Assert.That(turned.GetPixel(x, y).A, Is.EqualTo(255), $"({x},{y}) 不应露出空白");
    }

    [Test]
    public void FitTo_Nearest_KeepsHardEdgesWhenEnlarging()
    {
        // 4×2 黑白相间放大到 320×120：最近邻只有纯黑纯白，平滑插值会出现灰色过渡
        byte[] rgba = new byte[4 * 2 * 4];
        for (int i = 0; i < 8; i++)
        {
            byte v = (i + i / 4) % 2 == 0 ? (byte)0 : (byte)255;
            rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = v;
            rgba[i * 4 + 3] = 255;
        }
        PixelImage tiny = new(4, 2, rgba);
        SkiaImageProcessor processor = new();

        PixelImage nearest = processor.FitTo(tiny, 320, 120, ImageFit.Stretch, nearest: true);
        PixelImage smooth = processor.FitTo(tiny, 320, 120, ImageFit.Stretch);

        static int Grays(PixelImage image)
        {
            int count = 0;
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                    if (image.GetPixel(x, y).R is > 20 and < 235)
                        count++;
            return count;
        }
        Assert.That(Grays(nearest), Is.Zero);
        Assert.That(Grays(smooth), Is.GreaterThan(0));
    }
}