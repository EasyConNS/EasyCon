using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using EasyCon2.Avalonia.Core.Services;
using EasyCon2.Avalonia.Services;
using EasyCon2.Avalonia.ViewModels;
using EasyCon2.Avalonia.Views;
using CoreLogService = EasyCon2.Avalonia.Core.Services.LogService;
using DeviceService = EasyCon2.Avalonia.Services.DeviceService;
using CaptureService = EasyCon2.Avalonia.Services.CaptureService;
using ScriptService = EasyCon2.Avalonia.Services.ScriptService;
using ControllerService = EasyCon2.Avalonia.Services.ControllerService;

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
            var logService = new CoreLogService();
            var deviceService = new DeviceService(logService);
            var captureService = new CaptureService(logService);
            var scriptService = new ScriptService(deviceService, captureService, logService);
            var controllerService = new ControllerService(deviceService.GetDevice(), scriptService);
            IDialogService dialogService = new DialogService();
            IWindowService windowService = new WindowService(deviceService, logService, dialogService);
            var mainWindow = new MainWindow { DataContext = new MainWindowViewModel(logService, deviceService, captureService, scriptService, controllerService, dialogService, windowService) };
            desktop.MainWindow = mainWindow;
            desktop.Exit += (_, _) => controllerService.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
