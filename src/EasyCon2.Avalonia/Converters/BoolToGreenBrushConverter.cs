using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace EasyCon2.Avalonia.Converters;

public class BoolToGreenBrushConverter : IValueConverter
{
    private static readonly IBrush GreenBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return GreenBrush;
        return AvaloniaProperty.UnsetValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}