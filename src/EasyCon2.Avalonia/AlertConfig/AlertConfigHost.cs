namespace EasyCon2.Avalonia.AlertConfig;

public static class AlertConfigHost
{
    public static AlertConfigControl CreateControl(Action? onSave)
    {
        AvaloniaRuntime.EnsureInitialized();
        var vm = new AlertConfigViewModel { OnSaveCallback = onSave };
        var control = new AlertConfigControl { DataContext = vm };
        control.LoadData();
        control.DetachedFromVisualTree += (_, _) => vm.Dispose();
        return control;
    }
}