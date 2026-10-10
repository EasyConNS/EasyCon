using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace EasyCon2.Avalonia.Core.QQ;

public partial class QQGuideWindow : Window
{
    public QQGuideWindow()
    {
        InitializeComponent();
        DataContext = new QQGuideViewModel();
    }

    private async void OnEnlargeImage(object? sender, RoutedEventArgs e)
    {
        if (DataContext is QQGuideViewModel model)
        {
            QQGuideImageWindow window = new(model.CurrentStep);
            using IDisposable themeBinding = window.Bind(RequestedThemeVariantProperty,
                this.GetObservable(ActualThemeVariantProperty));
            await window.ShowDialog(this);
        }
    }

    private void OnReturnToConfiguration(object? sender, RoutedEventArgs e) => Close();

    private void OnStepSelectionChanged(object? sender, SelectionChangedEventArgs e) => StepScroll.Offset = default;
}