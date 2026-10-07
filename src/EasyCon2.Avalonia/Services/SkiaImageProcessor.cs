using EasyCon2.Avalonia.Services;
using SkiaSharp;

namespace EasyCon2.Avalonia.Services;

/// <summary>
/// <see cref="IImageProcessor"/> 的 SkiaSharp 实现（与 Avalonia 控件无耦合，
/// 编码路径与 headless 渲染测试一致，稳定不崩）。
/// </summary>
public sealed class SkiaImageProcessor : IImageProcessor
{
    public byte[]? Crop(byte[] sourceBytes, int x, int y, int width, int height)
    {
        if (sourceBytes == null || sourceBytes.Length == 0)
            return null;
        if (width <= 0 || height <= 0)
            return null;

        try
        {
            using var source = SKBitmap.Decode(sourceBytes);
            if (source == null)
                return null;

            x = Math.Clamp(x, 0, source.Width - 1);
            y = Math.Clamp(y, 0, source.Height - 1);
            width = Math.Clamp(width, 1, source.Width - x);
            height = Math.Clamp(height, 1, source.Height - y);

            using var image = SKImage.FromBitmap(source);
            using var subset = image.Subset(new SKRectI(x, y, x + width, y + height));
            if (subset == null)
                return null;

            using var data = subset.Encode(SKEncodedImageFormat.Png, 100);
            return data?.ToArray();
        }
        catch
        {
            return null;
        }
    }

    public byte[]? LoadAsPng(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return null;

            using var source = SKBitmap.Decode(filePath);
            if (source == null)
                return null;

            using var image = SKImage.FromBitmap(source);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data?.ToArray();
        }
        catch
        {
            return null;
        }
    }
}