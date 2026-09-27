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
}