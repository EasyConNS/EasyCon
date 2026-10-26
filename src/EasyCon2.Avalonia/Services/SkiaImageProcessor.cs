using EasyCon2.Avalonia.Models;
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

    public PixelImage? LoadPixels(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return null;

            using SKCodec? codec = SKCodec.Create(filePath);
            if (codec == null)
                return null;

            // 非预乘：半透明像素的 RGB 与 System.Drawing.Bitmap.GetPixel 读到的一致
            SKImageInfo info = new(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using SKBitmap bitmap = new(info);
            SKCodecResult result = codec.GetPixels(info, bitmap.GetPixels());
            if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
                return null;

            return new PixelImage(info.Width, info.Height, bitmap.Bytes);
        }
        catch
        {
            return null;
        }
    }

    public PixelImage FitTo(PixelImage source, int width, int height, ImageFit fit, CoverTransform? cover = null)
    {
        CoverTransform transform = fit == ImageFit.Cover ? cover ?? CoverTransform.Identity : CoverTransform.Identity;
        if (source.Width == width && source.Height == height && transform.IsIdentity)
            return source;

        SKImageInfo sourceInfo = new(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using SKImage image = SKImage.FromPixelCopy(sourceInfo, source.Rgba);
        // 光栅 Surface 只支持预乘；读回时再转成非预乘，与 LoadPixels 的约定一致
        using SKSurface surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Transparent);
        SKSamplingOptions sampling = new(SKCubicResampler.Mitchell);
        if (fit == ImageFit.Cover)
            DrawCover(surface.Canvas, image, source.Width, source.Height, width, height, transform, sampling);
        else
            surface.Canvas.DrawImage(image, Destination(source.Width, source.Height, width, height, fit), sampling);

        SKImageInfo targetInfo = new(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        byte[] rgba = new byte[width * height * 4];
        using SKImage snapshot = surface.Snapshot();
        unsafe
        {
            fixed (byte* pixels = rgba)
                snapshot.ReadPixels(targetInfo, (IntPtr)pixels, width * 4, 0, 0);
        }
        return new PixelImage(width, height, rgba);
    }

    /// <summary>
    /// 等比裁切：以画布中心为原点旋转、缩放后绘制。基础缩放取"旋转后仍能铺满画布"的最小值（0° 时即普通等比裁切），
    /// 再乘以 <see cref="CoverTransform.Zoom"/>；取景位置在图片超出画布的余量内移动。
    /// 有旋转时余量按外接矩形计算，平移到边缘可能露出角落的空白（透明，按白色处理）。
    /// </summary>
    private static void DrawCover(SKCanvas canvas, SKImage image, int sourceWidth, int sourceHeight, int width, int height,
        CoverTransform transform, SKSamplingOptions sampling)
    {
        double radians = transform.AngleDegrees * Math.PI / 180;
        double cos = Math.Abs(Math.Cos(radians));
        double sin = Math.Abs(Math.Sin(radians));
        double scale = Math.Max((width * cos + height * sin) / sourceWidth, (width * sin + height * cos) / sourceHeight)
            * Math.Max(1, transform.Zoom);
        double boundsWidth = scale * (sourceWidth * cos + sourceHeight * sin);
        double boundsHeight = scale * (sourceWidth * sin + sourceHeight * cos);
        // 取景位置向右（PanX > 0）意味着露出图片右侧，图片整体左移
        double dx = -Math.Clamp(transform.PanX, -1, 1) * Math.Max(0, (boundsWidth - width) / 2);
        double dy = -Math.Clamp(transform.PanY, -1, 1) * Math.Max(0, (boundsHeight - height) / 2);

        canvas.Save();
        canvas.Translate((float)(width / 2.0 + dx), (float)(height / 2.0 + dy));
        canvas.RotateDegrees((float)transform.AngleDegrees);
        canvas.Scale((float)scale);
        canvas.DrawImage(image, new SKRect(-sourceWidth / 2f, -sourceHeight / 2f, sourceWidth / 2f, sourceHeight / 2f), sampling);
        canvas.Restore();
    }

    private static SKRect Destination(int sourceWidth, int sourceHeight, int width, int height, ImageFit fit)
    {
        if (fit == ImageFit.Stretch)
            return new SKRect(0, 0, width, height);
        float scaleX = (float)width / sourceWidth;
        float scaleY = (float)height / sourceHeight;
        float scale = fit == ImageFit.Contain ? Math.Min(scaleX, scaleY) : Math.Max(scaleX, scaleY);
        float drawWidth = sourceWidth * scale;
        float drawHeight = sourceHeight * scale;
        float left = (width - drawWidth) / 2;
        float top = (height - drawHeight) / 2;
        return new SKRect(left, top, left + drawWidth, top + drawHeight);
    }

    public bool SavePng(PixelImage image, string filePath)
    {
        try
        {
            SKImageInfo info = new(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using SKImage encoded = SKImage.FromPixelCopy(info, image.Rgba);
            using SKData? data = encoded.Encode(SKEncodedImageFormat.Png, 100);
            if (data == null)
                return false;
            using FileStream stream = File.Create(filePath);
            data.SaveTo(stream);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}