using Avalonia.Controls;
using Avalonia.Interactivity;

namespace EasyCon2.Avalonia.Core.Mcp;

public partial class McpConfigControl : UserControl
{
    public event Action? SaveRequested;
    public event Action? CancelRequested;

    public McpConfigControl()
    {
        InitializeComponent();
    }

    public void LoadData()
    {
        if (DataContext is McpConfigViewModel vm)
            vm.Load();
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is McpConfigViewModel vm && vm.Save())
            SaveRequested?.Invoke();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        CancelRequested?.Invoke();
    }
}