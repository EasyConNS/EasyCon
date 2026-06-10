using Avalonia.Controls;
using EasyCon2.Avalonia.Core.AlertConfig;

namespace EasyCon2.Avalonia.Views;

public partial class AlertConfigWindow : Window
{
    public AlertConfigWindow()
    {
        InitializeComponent();

        var vm = new AlertConfigViewModel();
        AlertConfig.DataContext = vm;
        AlertConfig.LoadData();
        AlertConfig.SaveRequested += Close;
        AlertConfig.CancelRequested += Close;
    }
}