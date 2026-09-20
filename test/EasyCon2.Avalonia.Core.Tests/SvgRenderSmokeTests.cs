using SkiaSharp;
using Svg.Skia;

namespace EasyCon2.Avalonia.Core.Tests;

/// <summary>
/// 冒烟测试：确认 Svg.Skia 能正确解析并渲染我们的控制器 SVG
/// （其中使用了 &lt;symbol&gt;/&lt;use&gt;、CSS 类与 currentColor）。
/// </summary>
[TestFixture]
public class SvgRenderSmokeTests
{
    [Test]
    public void ControllerSvg_RendersNonEmptyPicture()
    {
        string path = Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "..",
            "src", "EasyCon2.Avalonia", "Resources", "Images", "ns2-controller.svg");

        Assert.That(File.Exists(path), Is.True, "SVG not found: " + Path.GetFullPath(path));

        using SKSvg svg = new();
        SKPicture? picture = svg.Load(path);
        Assert.That(picture, Is.Not.Null, "SKSvg.Load returned null (parse failed)");

        SKRect bounds = picture.CullRect;
        const float scale = 2f;
        int w = (int)Math.Ceiling(bounds.Width * scale);
        int h = (int)Math.Ceiling(bounds.Height * scale);

        using SKSurface surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(0x13, 0x14, 0x18));
        canvas.Scale(scale);
        canvas.Translate(-bounds.Left, -bounds.Top);
        canvas.DrawPicture(picture);

        using SKImage image = surface.Snapshot();
        string outPng = Path.Combine(Path.GetTempPath(), "opencode", "svgqa", "skia-rendered-controller.png");
        Directory.CreateDirectory(Path.GetDirectoryName(outPng)!);
        using (SKData data = image.Encode(SKEncodedImageFormat.Png, 100))
        using (FileStream fs = File.Create(outPng))
        {
            data.SaveTo(fs);
        }

        using SKBitmap bmp = SKBitmap.Decode(outPng)!;
        Assert.That(bmp, Is.Not.Null);
        int bright = 0;
        for (int y = 0; y < bmp.Height; y++)
        {
            for (int x = 0; x < bmp.Width; x++)
            {
                SKColor c = bmp.GetPixel(x, y);
                if (c.Red + c.Green + c.Blue > 200)
                {
                    bright++;
                }
            }
        }
        TestContext.Out.WriteLine($"rendered {w}x{h}, bright pixels = {bright}, png = {outPng}");
        Assert.That(bright, Is.GreaterThan(2000), "rendered picture has almost no visible strokes");
    }
}
