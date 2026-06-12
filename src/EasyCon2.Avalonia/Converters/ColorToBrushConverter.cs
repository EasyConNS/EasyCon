using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace EasyCon2.Avalonia.Converters;

/// <summary>
/// Converts a Color to a SolidColorBrush for binding to Background/Foreground.
/// </summary>
public class ColorToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is Color c ? new SolidColorBrush(c) : value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is SolidColorBrush b ? b.Color : value;
    }
}
