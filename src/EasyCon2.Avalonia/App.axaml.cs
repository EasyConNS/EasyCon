using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using EasyCon2.Avalonia.Core.Services;
using EasyCon2.Avalonia.Services;
using EasyCon2.Avalonia.ViewModels;
using EasyCon2.Avalonia.Views;
using CaptureService = EasyCon2.Avalonia.Services.CaptureService;
using ControllerService = EasyCon2.Avalonia.Services.ControllerService;
using CoreLogService = EasyCon2.Avalonia.Core.Services.LogService;
using DeviceService = EasyCon2.Avalonia.Services.DeviceService;
using ScriptService = EasyCon2.Avalonia.Services.ScriptService;

namespace EasyCon2.Avalonia;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var logService = new CoreLogService();
            var deviceService = new DeviceService(logService);
            var captureService = new CaptureService(logService);
            var scriptService = new ScriptService(deviceService, captureService, logService);
            // Linux 平台暂无底层控制实现，先用 Mock 占位；其它平台走真实实现。
            IControllerService controllerService = OperatingSystem.IsLinux()
                ? new MockControllerService()
                : new ControllerService(deviceService.GetDevice(), scriptService);
            IDialogService dialogService = new DialogService();
            IWindowService windowService = new WindowService(deviceService, logService, dialogService);
            var mainWindow = new MainWindow { DataContext = new MainWindowViewModel(logService, deviceService, captureService, scriptService, controllerService, dialogService, windowService) };
            desktop.MainWindow = mainWindow;

            // 注入主窗口引用，确保所有子窗口/弹窗/VPadOverlay 以主窗口为 Owner
            WindowService.MainWindow = mainWindow;
            controllerService.SetOwnerWindow(mainWindow);

            desktop.Exit += (_, _) => controllerService.Dispose();
        }

        // 默认启用简体中文（语言资源在 App.axaml 中以文化名为键注册，由 SetLocale 合并激活）。
        SetLocale("zh_CN");

        base.OnFrameworkInitializationCompleted();
    }
}