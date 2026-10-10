using Avalonia.Controls;
using Avalonia.Interactivity;
using EasyCon.Core.Notifications;
using EasyCon2.Avalonia.Core.QQ;

namespace EasyCon2.Avalonia.Views;

public partial class QQNotificationWindow : Window
{
    private readonly QQNotificationViewModel _viewModel;

    public QQNotificationWindow(QQNotificationService service)
    {
        InitializeComponent();
        _viewModel = new QQNotificationViewModel(service);
        DataContext = _viewModel;
        Closed += (_, _) => _viewModel.Dispose();
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}