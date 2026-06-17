using Avalonia.Data.Converters;
using System.Globalization;

namespace EasyCon2.Avalonia.Core.Converters;

/// <summary>
/// 布尔 → 眼睛 SVG path data（黑白线条矢量）。
/// false（遮蔽）→ 睁开的眼；true（明文）→ 带斜划线的眼。
/// 绑定到 Path.Data，控件 Foreground 决定颜色（默认黑色）。
/// </summary>
public class BoolToEyeIconConverter : IValueConverter
{
    public static readonly BoolToEyeIconConverter Instance = new();

    // 简化版「眼睛」轮廓（睁眼）
    private const string EyeOpen =
        "M12,4.5C7,4.5 2.73,7.61 1,12c1.73,4.39 6,7.5 11,7.5s9.27,-3.11 11,-7.5C21.27,7.61 17,4.5 12,4.5z " +
        "M12,17a5,5 0 1,1 0,-10a5,5 0 0,1 0,10z M12,14a2,2 0 1,1 0,-4a2,2 0 0,1 0,4z";

    // 简化版「眼睛」+ 斜划线（隐藏）
    private const string EyeOff =
        "M12,6.5c2.76,0 5,2.24 5,5c0,0.51 -0.1,1 -0.24,1.46l3.06,3.06c1.39,-1.23 2.49,-2.77 3.18,-4.52" +
        "C21.27,7.11 17,4 12,4c-1.27,0 -2.49,0.2 -3.64,0.57l2.17,2.17C10.96,6.64 11.47,6.5 12,6.5z " +
        "M2.71,3.16a0.996,0.996 0 0,0 0,1.41l1.97,1.97C3.06,7.79 1.69,9.79 1,12c1.73,4.39 6,7.5 11,7.5" +
        "c1.55,0 3.03,-0.3 4.38,-0.84l0.42,0.42L19.59,21l1.41,-1.41L4.13,2.75L2.71,3.16z " +
        "M9.51,11.37l1.41,1.41a2,2 0 0,0 2.83,-2.83l1.41,1.41a3.5,3.5 0 0,1 -5.66,0.01z";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? EyeOff : EyeOpen;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

