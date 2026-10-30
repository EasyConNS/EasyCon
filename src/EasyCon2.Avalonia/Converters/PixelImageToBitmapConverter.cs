using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using EasyCon2.Avalonia.Models;
using System.Globalization;

namespace EasyCon2.Avalonia.Converters;

/// <summary>
/// 已解码的 <see cref="PixelImage"/> → 可显示的位图，直接拷贝像素，不再重新编码/解码。
/// ViewModel 只持有与 UI 框架无关的像素数据。
/// </summary>
public class PixelImageToBitmapConverter : IValueConverter
{
    public static readonly PixelImageToBitmapConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not PixelImage image)
            return null;

        WriteableBitmap bitmap = new(new PixelSize(image.Width, image.Height), new Vector(96, 96),
            PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using ILockedFramebuffer buffer = bitmap.Lock();
        int rowLength = image.Width * 4;
        ReadOnlySpan<byte> source = image.Rgba;
        for (int y = 0; y < image.Height; y++)
        {
            Span<byte> target;
            unsafe
            {
                target = new Span<byte>((byte*)buffer.Address + y * buffer.RowBytes, rowLength);
            }
            source.Slice(y * rowLength, rowLength).CopyTo(target);
        }
        return bitmap;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}