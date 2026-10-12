using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace EasyCon2.Avalonia.QQ;

public partial class QQGuideImageWindow : Window
{
    public QQGuideImageWindow()
    {
        InitializeComponent();
    }

    public QQGuideImageWindow(QQGuideStep step) : this()
    {
        DataContext = step;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ImageScroll.UpdateLayout();
        if (DataContext is QQGuideStep step)
        {
            ImageScroll.Offset = new Vector(
                Math.Max(0, step.Crop.Center.X - (ImageScroll.Viewport.Width / 2)),
                Math.Max(0, step.Crop.Center.Y - (ImageScroll.Viewport.Height / 2)));
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}