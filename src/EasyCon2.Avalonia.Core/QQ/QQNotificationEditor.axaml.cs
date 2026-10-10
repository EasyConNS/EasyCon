using Avalonia.Controls;
using Avalonia.Interactivity;

namespace EasyCon2.Avalonia.Core.QQ;

public partial class QQNotificationEditor : UserControl
{
    public QQNotificationEditor()
    {
        InitializeComponent();
    }

    private async void OnOpenGuide(object? sender, RoutedEventArgs e)
    {
        QQGuideWindow guide = new();
        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            guide.RequestedThemeVariant = owner.ActualThemeVariant;
            await guide.ShowDialog(owner);
        }
        else
        {
            guide.Show();
        }
    }
}