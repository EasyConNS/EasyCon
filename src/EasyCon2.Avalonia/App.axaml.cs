using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using EasyCon.Capture;
using EasyCon.Core;
using EasyCon.Core.Capabilities;
using EasyCon.Core.Config;
using EasyCon.Core.Notifications;
using EasyCon2.Avalonia.AlertConfig;
using EasyCon2.Avalonia.Connection;
using EasyCon2.Avalonia.KeyMapping;
using EasyCon2.Avalonia.Mcp;
using EasyCon2.Avalonia.ModelsConfig;
using EasyCon2.Avalonia.Monitoring;
using EasyCon2.Avalonia.Scripting;
using EasyCon2.Avalonia.Services;
using EasyCon2.Avalonia.Shell;
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
            AlertService alertService = new(
                resultLogger: message => logService.Print(message, true),
                imageProvider: () =>
                {
                    using FrameLease? lease = captureService.AcquireLatestFrame();
                    return NotificationImage.FromFrame(lease?.Mat);
                });
            var scriptService = new ScriptService(deviceService, captureService, logService, alertService);
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

            // MCP Server：GUI 启动即后台监听 loopback /mcp，任意外部 agent 可驱动
            // （端口可用环境变量 EC_MCP_PORT 覆盖；设为 0 关闭）。危险工具不导出（fail-closed）。
            StartMcpServer();

            // Flow 服务：与 MCP 并排的第二个 loopback 端点（前端画布/外部工具的 HTTP 后端），
            // 同时把 FlowServiceTools 注册进 GUI 的 agent 工具注册中心（agent 与画布能力对等）。
            // 设备经桥接入监控页实例：画布/agent 的 flow_* 工具与 GUI 操作同一实例。
            StartFlowService(deviceService, captureService);

            desktop.Exit += (_, _) =>
            {
                // 单一清理入口：关窗路径只做 UI/配置收尾，服务释放统一在此（带防御）
                try { if (controllerService is IDisposable cd) cd.Dispose(); } catch { }
                try { alertService.Dispose(); } catch { }
                try { captureService.Dispose(); } catch { }
                try { deviceService.Dispose(); } catch { }
                try { _mcpHttpServer?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2)); } catch { }
                try { _flowHttp?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2)); } catch { }
                try { _flowState?.Dispose(); } catch { }
                logService.Dispose();
                EasyCon.Core.Logging.CoreLog.Sink = null;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static EasyCon.Core.LLM.Agent.Mcp.McpHttpServer? _mcpHttpServer;
    private static EasyCon.Core.Flow.FlowServiceState? _flowState;
    private static EasyCon.Core.Flow.FlowServiceHttp? _flowHttp;

    /// <summary>
    /// Flow 编排服务（HTTP，loopback）：前端 Python 画布与外部工具的后端。
    /// 端口由 <c>EC_FLOW_PORT</c> 覆盖（默认 19391，0 = 关闭）。
    ///
    /// <para>
    /// 设备桥接：画布/agent 的 flow_* 设备工具经 <see cref="EasyCon.Core.Flow.FlowDeviceBridge"/>
    /// 直接驱动 GUI 监控页持有的采集源/单片机实例——画布、agent（flow_*、get_frame）与 GUI
    /// 是同一实例，连接状态实时一致，一块采集卡/串口只开一次。
    /// </para>
    /// </summary>
    private static void StartFlowService(DeviceService deviceService, CaptureService captureService)
    {
        var port = int.TryParse(Environment.GetEnvironmentVariable("EC_FLOW_PORT"), out var p) ? p : 19391;
        if (port <= 0)
        {
            EasyCon.Core.Logging.CoreLog.Info("[FlowService] EC_FLOW_PORT<=0，已禁用 HTTP Flow 服务");
            return;
        }

        // 桥接 GUI 监控页的设备实例（DisconnectVideo 阻塞等待采集循环退出，勿在 UI 线程调用）
        _flowState = new EasyCon.Core.Flow.FlowServiceState(new EasyCon.Core.Flow.FlowDeviceBridge
        {
            IsVideoConnected = () => captureService.IsConnected,
            ConnectVideo = index => captureService.TryConnect(index)
                ? null
                : $"视频源打开失败: [{index}]",
            DisconnectVideo = () => captureService.DisconnectAsync().GetAwaiter().GetResult(),
            GetFrameStore = () => captureService.GetFrameStore(),
            IsMcuConnected = () => deviceService.IsConnected,
            ConnectMcu = port => deviceService.TryConnect(port)
                ? null
                : $"单片机连接失败: {port}",
            DisconnectMcu = () => deviceService.Disconnect(),
            GetPad = () => deviceService.IsConnected
                ? new PadInputAdapter(new GamePadAdapter(deviceService.GetDevice(), highResolution: false))
                : null,
        });

        // 与 MCP 共用同一注册中心：GUI 内的 agent 也能用画布那套设备/图工具
        var registry = EasyCon2.Avalonia.AiAgent.AiAgentViewModel.SharedTools;
        if (registry is not null)
            EasyCon.Core.Flow.FlowServiceTools.RegisterAll(registry, _flowState);
        else
            EasyCon.Core.Logging.CoreLog.Info("[FlowService] 无工具注册中心，未注册 flow_* 工具");

        _ = Task.Run(async () =>
        {
            try
            {
                _flowHttp = new EasyCon.Core.Flow.FlowServiceHttp(_flowState, port);
                await _flowHttp.StartAsync(CancellationToken.None);
                EasyCon.Core.Logging.CoreLog.Info($"[FlowService] 已监听 {_flowHttp.Url}（画布后端）");
            }
            catch (Exception ex)
            {
                EasyCon.Core.Logging.CoreLog.Error($"[FlowService] 启动失败（端口 {port}）: {ex.Message}");
            }
        });
    }

    private static void StartMcpServer()
    {
        var port = int.TryParse(Environment.GetEnvironmentVariable("EC_MCP_PORT"), out var p) ? p : 19390;
        if (port <= 0)
        {
            EasyCon.Core.Logging.CoreLog.Info("[McpServer] EC_MCP_PORT<=0，已禁用 HTTP MCP");
            return;
        }

        var registry = EasyCon2.Avalonia.AiAgent.AiAgentViewModel.SharedTools;
        if (registry is null)
        {
            EasyCon.Core.Logging.CoreLog.Info("[McpServer] 无工具注册中心，跳过 HTTP MCP");
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                _mcpHttpServer = await EasyCon.Core.LLM.Agent.Mcp.McpServerHost.StartHttpAsync(
                    registry, "easycon-gui", "1.0", port, CancellationToken.None);
            }
            catch (Exception ex)
            {
                EasyCon.Core.Logging.CoreLog.Error($"[McpServer] HTTP MCP 启动失败: {ex.Message}");
            }
        });
    }
}