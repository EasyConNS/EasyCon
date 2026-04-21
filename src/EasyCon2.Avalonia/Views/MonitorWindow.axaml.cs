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

        Opened += (s, e) =>
        {
            vm.StartMonitoring();
        };

        Closing += (s, e) =>
        {
            vm.Close();
        };
    }
}