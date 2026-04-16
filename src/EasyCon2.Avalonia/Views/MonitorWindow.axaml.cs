using Avalonia;
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
        var vm = new MonitorWindowViewModel(captureService);
        DataContext = vm;

        // 先设置 DPI，再启动监视，避免首帧渲染时 DPI 未初始化的问题
        Opened += (s, e) =>
        {
            var scaling = RenderScaling;
            var dpi = new Vector(96 * scaling, 96 * scaling);
            vm.SetRenderDpi(dpi);
            vm.StartMonitoring();
        };

        Closing += (s, e) =>
        {
            vm.Close();
        };
    }
}
