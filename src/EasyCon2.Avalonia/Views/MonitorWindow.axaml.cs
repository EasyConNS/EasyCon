using Avalonia.Controls;
using EasyCon2.Avalonia.Services;
using EasyCon2.Avalonia.ViewModels;

namespace EasyCon2.Avalonia.Views;

public partial class MonitorWindow : Window
{
    public MonitorWindow()
    {
        InitializeComponent();
    }

    public MonitorWindow(ICaptureService captureService)
    {
        InitializeComponent();
        this.DataContext = new MonitorWindowViewModel(captureService);

        // 窗口关闭时停止监视器
        this.Closing += (s, e) =>
        {
            if (DataContext is MonitorWindowViewModel vm)
            {
                vm.StopMonitoring();
            }
        };
    }
}
