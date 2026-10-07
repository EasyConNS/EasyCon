using Avalonia.Controls;
using Avalonia.Interactivity;
using EasyCon.Core.Config;

namespace EasyCon2.Avalonia.ModelsConfig;

public partial class ModelsConfigControl : UserControl
{
    public event Action? SaveRequested;
    public event Action? CancelRequested;

    public ModelsConfigControl()
    {
        InitializeComponent();
    }

    public void LoadData()
    {
        if (DataContext is ModelsConfigViewModel vm)
            vm.Load();
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ModelsConfigViewModel vm && vm.Save())
            SaveRequested?.Invoke();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        CancelRequested?.Invoke();
    }

    private void OnOpenFile(object? sender, RoutedEventArgs e)
    {
        var dir = Path.GetDirectoryName(AppPaths.ModelsConfig);
        if (dir != null && Directory.Exists(dir))
            Native.FileManager.OpenInFileManager(dir);
    }
}