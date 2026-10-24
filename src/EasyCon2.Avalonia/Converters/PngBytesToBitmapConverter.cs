using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using System.Globalization;

namespace EasyCon2.Avalonia.Converters;

/// <summary>
/// PNG 字节 → Bitmap（ViewModel 只持有字节，不接触 Avalonia 图像类型）。解码失败返回 null。
/// </summary>
public class PngBytesToBitmapConverter : IValueConverter
{
    public static readonly PngBytesToBitmapConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not byte[] bytes || bytes.Length == 0)
            return null;
        try
        {
            return new Bitmap(new MemoryStream(bytes));
        }
        catch
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}