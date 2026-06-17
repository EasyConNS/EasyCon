using Avalonia.Data.Converters;
using System.Globalization;

namespace EasyCon2.Avalonia.Core.Converters;

/// <summary>
/// 布尔 → char 转换。默认：true（隐藏）→ '•'，false（显示）→ '\0'（无遮蔽）。
/// 可通过 parameter 自定义遮蔽字符。
/// </summary>
public class BoolToMaskCharConverter : IValueConverter
{
    public static readonly BoolToMaskCharConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // IsApiKeyVisible=true 表示明文显示 → 无遮蔽字符
        var isHidden = value is bool b && !b;
        if (!isHidden) return '\0';

        return parameter is string s && s.Length > 0 ? s[0] : '•';
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
