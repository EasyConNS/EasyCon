namespace EasyCon2.Avalonia.Core.AlertConfig;

public static class AlertConfigHost
{
    public static AlertConfigControl CreateControl(Action? onSave)
    {
        AvaloniaRuntime.EnsureInitialized();
        var vm = new AlertConfigViewModel { OnSaveCallback = onSave };
        var control = new AlertConfigControl { DataContext = vm };
        control.LoadData();
        return control;
    }
}