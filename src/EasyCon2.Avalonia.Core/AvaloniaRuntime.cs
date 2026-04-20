using Avalonia;

namespace EasyCon2.Avalonia.Core;

public static class AvaloniaRuntime
{
    private static bool _initialized;

    public static void EnsureInitialized()
    {
        if (_initialized) return;
        AppBuilder.Configure<Application>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .SetupWithoutStarting();
        _initialized = true;
    }
}
