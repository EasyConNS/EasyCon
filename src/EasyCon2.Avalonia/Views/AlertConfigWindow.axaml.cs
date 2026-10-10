using Avalonia.Controls;
using EasyCon2.Avalonia.Core.AlertConfig;

namespace EasyCon2.Avalonia.Views;

public partial class AlertConfigWindow : Window
{
    public AlertConfigWindow() : this(new AlertConfigViewModel())
    {
        AlertConfig.LoadData();
    }

    public AlertConfigWindow(AlertConfigViewModel vm)
    {
        InitializeComponent();

        AlertConfig.DataContext = vm;
        AlertConfig.SaveRequested += Close;
        AlertConfig.CancelRequested += Close;

        Closed += (_, _) => vm.Dispose();
    }
}