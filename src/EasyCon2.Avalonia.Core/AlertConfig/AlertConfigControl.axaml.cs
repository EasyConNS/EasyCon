using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using EasyCon.Core.Config;

namespace EasyCon2.Avalonia.Core.AlertConfig;

public partial class AlertConfigControl : UserControl
{
    public event Action? SaveRequested;
    public event Action? CancelRequested;

    public AlertConfigControl()
    {
        InitializeComponent();
    }

    public void LoadData()
    {
        if (DataContext is AlertConfigViewModel vm)
            vm.Load();
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AlertConfigViewModel vm)
            vm.Save();
        SaveRequested?.Invoke();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        CancelRequested?.Invoke();
    }

    private void OnOpenFile(object? sender, RoutedEventArgs e)
    {
        var path = AppPaths.AlertConfig;
        if (File.Exists(path))
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
