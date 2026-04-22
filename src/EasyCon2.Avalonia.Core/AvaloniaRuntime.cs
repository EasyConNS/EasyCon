using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;

namespace EasyCon2.Avalonia.Core;

public static class AvaloniaRuntime
{
    private static bool _initialized;

    public static void EnsureInitialized()
    {
        if (_initialized) return;
        var app = AppBuilder.Configure<Application>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .SetupWithoutStarting();
        app.Instance.Styles.Add(new FluentTheme());

        var editStyle = new StyleInclude(new Uri("avares://EasyCon2.Avalonia.Core"))
        {
            Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml")
        };
        app.Instance.Styles.Add(editStyle);

        _initialized = true;
    }
}