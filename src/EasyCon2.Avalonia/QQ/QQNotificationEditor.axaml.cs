using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace EasyCon2.Avalonia.QQ;

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
            using IDisposable themeBinding = guide.Bind(Window.RequestedThemeVariantProperty,
                owner.GetObservable(Window.ActualThemeVariantProperty));
            await guide.ShowDialog(owner);
        }
        else
        {
            guide.Show();
        }
    }
}