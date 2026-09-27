namespace EasyCon2.Avalonia.Core.ModelsConfig;

public static class ModelsConfigHost
{
    public static ModelsConfigControl CreateControl(Action? onSave)
    {
        AvaloniaRuntime.EnsureInitialized();
        var vm = new ModelsConfigViewModel { OnSaveCallback = onSave };
        var control = new ModelsConfigControl { DataContext = vm };
        control.LoadData();
        return control;
    }
}