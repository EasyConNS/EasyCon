using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using EasyCon2.Avalonia.Services;
using EasyCon2.Avalonia.ViewModels;
using EasyCon2.Avalonia.Views;

namespace EasyCon2.Avalonia;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var logService = new LogService();
            var deviceService = new DeviceService(logService);
            var captureService = new CaptureService(logService);
            var controllerService = new ControllerService();
            var scriptService = new ScriptService(deviceService, captureService, logService);
            desktop.MainWindow = new MainWindow { DataContext = new MainWindowViewModel(logService, deviceService, captureService, scriptService, controllerService) };
        }

        base.OnFrameworkInitializationCompleted();
    }
}