using Avalonia.Controls;
using EasyCon.Core.Notifications;
using EasyCon2.Avalonia.Core.AlertConfig;
using EasyCon2.Avalonia.Core.QQ;

namespace EasyCon2.Avalonia.Views;

public partial class AlertConfigWindow : Window
{
    private readonly QQNotificationViewModel? _qqViewModel;

    public AlertConfigWindow() : this(null)
    {
    }

    public AlertConfigWindow(QQNotificationService? qqNotificationService)
    {
        InitializeComponent();

        var vm = new AlertConfigViewModel();
        AlertConfig.DataContext = vm;
        AlertConfig.LoadData();
        AlertConfig.SaveRequested += Close;
        AlertConfig.CancelRequested += Close;

        if (qqNotificationService != null)
        {
            _qqViewModel = new QQNotificationViewModel(qqNotificationService);
            QqConfig.DataContext = _qqViewModel;
            QqConfig.CloseRequested += Close;
            Closed += (_, _) => _qqViewModel.Dispose();
        }
        else
        {
            QqTab.IsVisible = false;
            NotificationChannels.SelectedIndex = 1;
        }
    }
}