using Avalonia;
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
        _initialized = true;
    }
}