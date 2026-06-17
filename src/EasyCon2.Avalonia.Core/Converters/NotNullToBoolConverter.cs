using Avalonia.Data.Converters;
using System.Globalization;

namespace EasyCon2.Avalonia.Core.Converters;

/// <summary>
/// 非空字符串 → true（用于 IsVisible 绑定到可能为 null 的状态文本）。
/// </summary>
public class NotNullToBoolConverter : IValueConverter
{
    public static readonly NotNullToBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrWhiteSpace(value as string);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
