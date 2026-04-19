using Avalonia;

namespace EasyCon2.Avalonia.Core.AlertConfig;

public static class AlertConfigHost
{
    private static bool _initialized;

    public static void EnsureInitialized()
    {
        if (_initialized) return;

        AppBuilder.Configure<AlertConfigApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .SetupWithoutStarting();

        _initialized = true;
    }

    public static AlertConfigControl CreateControl(Action? onSave)
    {
        EnsureInitialized();
        var vm = new AlertConfigViewModel { OnSaveCallback = onSave };
        var control = new AlertConfigControl { DataContext = vm };
        control.LoadData();
        return control;
    }
}
