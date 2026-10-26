using EasyCon2.Avalonia.Models;
using EasyCon2.Avalonia.Services;
using SkiaSharp;

namespace EasyCon2.Avalonia.UiTests;

/// <summary>
/// SkiaImageProcessor 的裁剪/加载行为（PNG 字节进出，纯 SkiaSharp，无窗口服务）。
/// </summary>
[TestFixture]
public class SkiaImageProcessorTests
{
    private static byte[] CreateTestPng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                bitmap.SetPixel(x, y, x < width / 2 ? SKColors.Red : SKColors.Blue);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Test]
    public void Crop_ReturnsRequestedSize_AndClampsToSource()
    {
        var processor = new SkiaImageProcessor();
        var source = CreateTestPng(100, 80);

        var cropped = processor.Crop(source, 10, 20, 40, 30);
        Assert.That(cropped, Is.Not.Null);

        using var decoded = SKBitmap.Decode(cropped!);
        Assert.Multiple(() =>
        {
            Assert.That(decoded, Is.Not.Null);
            Assert.That(decoded!.Width, Is.EqualTo(40));
            Assert.That(decoded.Height, Is.EqualTo(30));
        });
    }

    [Test]
    public void Crop_ClampsOversizedRectangles_ToSourceBounds()
    {
        var processor = new SkiaImageProcessor();
        var source = CreateTestPng(50, 50);

        var cropped = processor.Crop(source, 40, 40, 100, 100);
        Assert.That(cropped, Is.Not.Null);

        using var decoded = SKBitmap.Decode(cropped!);
        Assert.Multiple(() =>
        {
            Assert.That(decoded!.Width, Is.EqualTo(10), "超出源图的宽应被钳制到右边界");
            Assert.That(decoded.Height, Is.EqualTo(10), "超出源图的高应被钳制到下边界");
        });
    }

    [Test]
    public void Crop_WithInvalidInput_ReturnsNull()
    {
        var processor = new SkiaImageProcessor();

        Assert.Multiple(() =>
        {
            Assert.That(processor.Crop([], 0, 0, 10, 10), Is.Null);
            Assert.That(processor.Crop(CreateTestPng(50, 50), 0, 0, 0, 10), Is.Null);
            Assert.That(processor.Crop([0x00, 0x01, 0x02], 0, 0, 10, 10), Is.Null, "非图像字节应返回 null 而非抛出");
        });
    }

    [Test]
    public void LoadAsPng_FromFile_ReturnsPngBytes()
    {
        var processor = new SkiaImageProcessor();
        var source = CreateTestPng(32, 16);
        var path = Path.Combine(Path.GetTempPath(), $"easycon-img-{Guid.NewGuid():N}.png");
        try
        {
            File.WriteAllBytes(path, source);

            var loaded = processor.LoadAsPng(path);
            Assert.That(loaded, Is.Not.Null);

            using var decoded = SKBitmap.Decode(loaded!);
            Assert.That(decoded!.Width, Is.EqualTo(32));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void LoadAsPng_WithMissingFile_ReturnsNull()
    {
        var processor = new SkiaImageProcessor();
        Assert.That(processor.LoadAsPng("no/such/file.png"), Is.Null);
    }

    /// <summary>左半黑、右半白的不透明图。</summary>
    private static PixelImage HalfBlack(int width, int height)
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
    public void SavePng_RoundTripsPixelsAndTheBinarizedResult()
    {
        SkiaImageProcessor processor = new();
        byte[] rgba = new byte[320 * 120 * 4];
        for (int i = 0; i < rgba.Length; i += 4)
        {
            rgba[i] = (byte)(i * 7);
            rgba[i + 1] = (byte)(i * 13);
            rgba[i + 2] = (byte)(i * 3);
            rgba[i + 3] = (byte)(i % 3 == 0 ? 255 : 128 + i % 100);
        }
        PixelImage original = new(320, 120, rgba);
        EasyCon2.Avalonia.DrawingBoard.InkMask mask = EasyCon2.Avalonia.DrawingBoard.InkMask.Binarize(original,
            new EasyCon2.Avalonia.DrawingBoard.BinarizeOptions(50, false, EasyCon2.Avalonia.DrawingBoard.DitherMode.Atkinson));
        string directory = Path.Combine(Path.GetTempPath(), "easycon-save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string originalPath = Path.Combine(directory, "original.png");
            string maskPath = Path.Combine(directory, "mask.png");

            Assert.That(processor.SavePng(original, originalPath), Is.True);
            Assert.That(processor.SavePng(mask.ToPreview(), maskPath), Is.True);

            PixelImage reloaded = processor.LoadPixels(originalPath)!;
            Assert.That(reloaded.Rgba.ToArray(), Is.EqualTo(original.Rgba.ToArray()), "PNG 无损，含半透明像素");

            PixelImage reloadedMask = processor.LoadPixels(maskPath)!;
            EasyCon2.Avalonia.DrawingBoard.InkMask again = EasyCon2.Avalonia.DrawingBoard.InkMask.Binarize(reloadedMask,
                new EasyCon2.Avalonia.DrawingBoard.BinarizeOptions(50, false));
            for (int row = 0; row < 120; row++)
                for (int col = 0; col < 320; col++)
                    Assert.That(again[col, row], Is.EqualTo(mask[col, row]), $"({col},{row})");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void SavePng_UnwritablePath_ReturnsFalse()
    {
        PixelImage image = new(1, 1, [0, 0, 0, 255]);

        Assert.That(new SkiaImageProcessor().SavePng(image, Path.Combine(Path.GetTempPath(), "missing-dir-" + Guid.NewGuid().ToString("N"), "x.png")), Is.False);
    }
}