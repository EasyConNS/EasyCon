using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace EasyCon2.Avalonia.Core.AlertConfig;

public partial class AlertItemView : UserControl
{
    public static readonly string[] HttpMethods = ["GET", "POST", "PUT", "HEAD", "OPTIONS"];

    public AlertItemView()
    {
        InitializeComponent();
    }

    private void OnNameClick(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is AlertItemViewModel vm)
            vm.ToggleExpandCommand.Execute(null);
    }
}