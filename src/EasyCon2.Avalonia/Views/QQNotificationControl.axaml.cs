using Avalonia.Controls;
using Avalonia.Interactivity;

namespace EasyCon2.Avalonia.Views;

public partial class QQNotificationControl : UserControl
{
    public event Action? CloseRequested;

    public QQNotificationControl()
    {
        InitializeComponent();
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke();
    }
}