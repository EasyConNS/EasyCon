using System.ComponentModel;
using System.Globalization;
using Avalonia.Data.Converters;
using EasyCon.Capture;

namespace EasyCon2.Avalonia.Core.Converters;

public class SearchMethodDescriptionConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SearchMethod method)
        {
            return method.GetType().GetField(method.ToString())?
                .GetCustomAttributes(typeof(DescriptionAttribute), false)
                .Cast<DescriptionAttribute>()
                .FirstOrDefault()?.Description ?? method.ToString();
        }
        return value?.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
