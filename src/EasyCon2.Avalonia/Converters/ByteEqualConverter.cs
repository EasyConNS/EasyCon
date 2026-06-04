using Avalonia.Data.Converters;
using System.Globalization;

namespace EasyCon2.Avalonia.Converters;

/// <summary>
/// 将 byte 值与 ConverterParameter 比较，相等返回 true。用于 RadioButton 绑定。
/// </summary>
public class ByteEqualConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is byte b && parameter is string s && byte.TryParse(s, out var target))
            return b == target;
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter is string s && byte.TryParse(s, out var target))
            return target;
        return global::Avalonia.AvaloniaProperty.UnsetValue;
    }
}