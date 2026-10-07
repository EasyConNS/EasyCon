using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using EasyCon.Core.Config;
using EasyCon2.Avalonia.Services;
using EasyCon2.Avalonia.Shell;
using EasyCon2.Avalonia.Monitoring;
using EasyCon2.Avalonia.Connection;
using EasyCon2.Avalonia.KeyMapping;
using EasyCon2.Avalonia.Scripting;
using EasyCon2.Avalonia.AlertConfig;
using EasyCon2.Avalonia.Mcp;
using EasyCon2.Avalonia.ModelsConfig;
using ControllerService = EasyCon2.Avalonia.Services.ControllerService;
using CoreLogService = EasyCon2.Avalonia.Services.LogService;
using DeviceService = EasyCon2.Avalonia.Services.DeviceService;

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
            var uiDispatcher = AvaloniaUiDispatcher.Instance;
            var logService = new CoreLogService(uiDispatcher);

            // 全局异常兜底：写入日志文件，避免崩溃/异常静默无迹可查
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                logService.AddLog($"[未处理异常] {e.ExceptionObject}");
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                logService.AddLog($"[未观察任务异常] {e.Exception.Message}");
                e.SetObserved();
            };
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                logService.AddLog($"[UI线程异常] {e.Exception.Message}\n{e.Exception.StackTrace}");
                // 不标记 Handled 的话异常仍会沿 Avalonia 默认流程终止进程，上面的日志只起到“临终遗言”作用
                e.Handled = true;
            };

            // 配置文件损坏时（已备份并改用默认值）向用户暴露
            ConfigManager.ConfigErrorReported += (path, message) =>
                logService.AddLog($"[配置] {path}: {message}");

            // LSP 客户端诊断路由到日志文件（Release 下此前完全无输出）
            EasyCon2.Avalonia.Editor.Lsp.LspClientService.LogSink = message => logService.AddLog(message);

            // AI Agent 诊断路由到日志文件（替代丢失的 Console 输出）
            EasyCon2.Avalonia.AiAgent.AiAgentViewModel.DiagLog = message => logService.AddLog(message);

            // 全局命令诊断路由到日志文件
            LogSink = message => logService.AddLog(message);

            // SDL 事件循环在后台线程执行处理器，未捕获异常会终止整个进程，接到日志兜底
            EasyCon.SDLInput.SdlEventLoop.HandlerException = ex =>
                logService.AddLog($"[SDL事件] 处理器异常: {ex.Message}");

            // 语言字典必须在任何 ViewModel 构造之前合并：MainWindowViewModel 的
            // 字段初始化器与 static readonly 文本会对 L10n.T 做一次性快照，
            // 晚于合并的话快照到的是 key 原文（且 static 快照进程内不会再更新）。
            SetLocale("zh_CN");

            // Core 层 VM 的本地化桥接到 App 的 L10n（保持单一译文来源）
            EasyCon2.Avalonia.Localization.L10nBridge.Resolver = EasyCon2.Avalonia.Markup.L10n.T;

            // 库层（Capture/Script 等）诊断转发到日志文件
            EasyCon.Core.Logging.CoreLog.Sink = message => logService.AddLog(message);

            var deviceService = new DeviceService(logService);
            var captureService = new CaptureService(logService);
            var scriptService = new ScriptService(deviceService, captureService, logService);
            // Linux 平台暂无底层控制实现，先用 Mock 占位；其它平台走真实实现。
            IControllerService controllerService = OperatingSystem.IsLinux()
                ? new MockControllerService()
                : new ControllerService(deviceService.GetDevice(), scriptService);
            IDialogService dialogService = new DialogService();
            var windowService = new WindowService(deviceService, logService, dialogService);
            var mainWindow = new MainWindow { DataContext = new MainWindowViewModel(logService, deviceService, captureService, scriptService, controllerService, dialogService, windowService, uiDispatcher, new SkiaImageProcessor()) };
            desktop.MainWindow = mainWindow;
            // 默认 OnLastWindowClose 下，键盘映射等子窗口或 VPadOverlay 存活时关主窗口
            // 不会触发 desktop.Exit，vpad 清理永远不执行（残留 SDL 钩子 + Topmost 悬浮窗）。
            // 主窗口关闭即应用关停，剩余窗口由框架强制关闭。
            desktop.ShutdownMode = global::Avalonia.Controls.ShutdownMode.OnMainWindowClose;

            // 注入主窗口 Owner，子窗口/弹窗/VPadOverlay 以主窗口为 Owner
            windowService.Owner = mainWindow;
            ((DialogService)dialogService).Owner = mainWindow;
            controllerService.SetOwnerWindow(mainWindow);

            // 预热按键映射窗口所需的资源（Icons.json / 控制器 SVG），避免首次打开时延迟闪现
            UiPreloader.Warmup();

            desktop.Exit += (_, _) =>
            {
                // 单一清理入口：关窗路径只做 UI/配置收尾，服务释放统一在此（带防御）
                try { if (controllerService is IDisposable cd) cd.Dispose(); } catch { }
                try { captureService.Dispose(); } catch { }
                try { deviceService.Dispose(); } catch { }
                logService.Dispose();
                EasyCon.Core.Logging.CoreLog.Sink = null;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}