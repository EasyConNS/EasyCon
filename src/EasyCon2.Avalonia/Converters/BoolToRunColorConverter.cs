using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace EasyCon2.Avalonia.Converters;

public class BoolToRunColorConverter : IValueConverter
{
    private static readonly IBrush RunBrush = new SolidColorBrush(Color.FromRgb(0x1F, 0x8A, 0x65));
    private static readonly IBrush StopBrush = new SolidColorBrush(Color.FromRgb(0xCF, 0x2D, 0x56));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool running)
            return running ? StopBrush : RunBrush;
        return RunBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}