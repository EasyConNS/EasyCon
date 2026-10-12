using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using EasyCon.Capture;
using EasyCon.Capture.Audio;
using EasyCon.Core;
using EasyCon.Core.Capabilities;
using EasyCon.Core.Config;
using EasyCon.Core.Notifications;
using EasyCon2.Avalonia.AlertConfig;
using EasyCon2.Avalonia.AudioLabels;
using EasyCon2.Avalonia.Connection;
using EasyCon2.Avalonia.KeyMapping;
using EasyCon2.Avalonia.Mcp;
using EasyCon2.Avalonia.ModelsConfig;
using EasyCon2.Avalonia.Monitoring;
using EasyCon2.Avalonia.Scripting;
using EasyCon2.Avalonia.Services;
using EasyCon2.Avalonia.Shell;
using EasyCon2.Avalonia.VPad;
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
            WireDiagnosticSinks(logService);

            // 语言字典必须在任何 ViewModel 构造之前合并（晚于合并会对 L10n.T 快照到 key 原文，且进程内不再更新）。
            SetLocale("zh_CN");

            // Core 层 VM 的本地化桥接到 App 的 L10n（保持单一译文来源）
            EasyCon2.Avalonia.Localization.L10nBridge.Resolver = EasyCon2.Avalonia.Markup.L10n.T;

            var maaHost = new EasyCon.Core.Maa.MaaHostSession();
            var deviceService = new DeviceService(logService, maaHost.Controller);
            var captureService = new CaptureService(logService, maaHost.Frames);
            AlertService alertService = new(
                resultLogger: message => logService.Print(message, true),
                imageProvider: () =>
                {
                    using FrameLease? lease = captureService.AcquireLatestFrame();
                    return NotificationImage.FromFrame(lease?.Mat);
                });
            var audioSource = new WasapiAudioCapture();
            AudioHistoryBuffer audioHistory = new(audioSource);
            var scriptService = new ScriptService(deviceService, captureService, logService, alertService);
            // Linux 平台暂无底层控制实现，先用 Mock 占位；VPadService（虚拟手柄）改为组合根构造后
            // 注入 ControllerService（其 ControllerAdapter 依赖 IScriptService，装配留在 App 侧），
            // 使 Services 层零 VPad 反向引用。
            var gamepad = deviceService.GetDevice();
            IControllerService controllerService = OperatingSystem.IsLinux()
                ? new MockControllerService()
                : new ControllerService(gamepad, new VPadService(gamepad, new VPadControllerAdapter(scriptService)));
            IDialogService dialogService = new DialogService();
            var windowService = new WindowService(logService);

            RegisterWindows(windowService, deviceService, logService, dialogService);

            var mainWindowViewModel = new MainWindowViewModel(logService, deviceService, captureService, scriptService, controllerService, dialogService, windowService, uiDispatcher, new SkiaImageProcessor(), updateService: new GiteeUpdateService(), audioSource: audioSource);
            windowService.Register(WindowKeys.AudioLabels, () =>
            {
                AudioLabelEditorViewModel editor = new(audioHistory, new AudioPreview(), dialogService,
                    () => mainWindowViewModel.CurrentScriptPath);
                AudioLabelEditorWindow window = new() { DataContext = editor };
                window.Closed += (_, _) => editor.Dispose();
                window.Activated += (_, _) => editor.RefreshLocalizedTexts();
                editor.CaptureRecentCommand.Execute(null);
                return window;
            });
            var mainWindow = new MainWindow { DataContext = mainWindowViewModel };
            desktop.MainWindow = mainWindow;
            // 显式关停，不用 OnMainWindowClose：其 TryShutdown 会被关停瞬间仍在窗口列表里的
            // 窗口（未保存确认弹窗、键盘映射等子窗口、VPadOverlay）一次性取消
            // （DoShutdown 见 Windows.Count>0 即 e.Cancel 且后续窗口关闭不再重试），
            // 进程就此驻留后台主循环。主窗口 Closed 后强制 Shutdown 可绕过该计数。
            desktop.ShutdownMode = global::Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
            mainWindow.Closed += (_, _) => Dispatcher.UIThread.Post(() => desktop.Shutdown());

            // 注入主窗口 Owner，子窗口/弹窗/VPadOverlay 以主窗口为 Owner
            windowService.Owner = mainWindow;
            ((DialogService)dialogService).Owner = mainWindow;
            controllerService.SetOwnerWindow(mainWindow);

            // 预热按键映射窗口所需的资源（Icons.json / 控制器 SVG），避免首次打开时延迟闪现
            UiPreloader.Warmup();

            StartLoopbackServices(mainWindowViewModel, deviceService, captureService);
            WireExitCleanup(desktop, controllerService, captureService, deviceService, audioSource, audioHistory, alertService, logService, maaHost);
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// 诊断 Sink 接线：全局异常兜底、配置错误与库层诊断输出统一路由到日志文件（只拆组，不改编译期语义）。
    /// AI Agent 的 diagLog 经 MainWindowViewModel 构造参数注入（原为 AiAgentViewModel.DiagLog 静态汇点）。
    /// </summary>
    private static void WireDiagnosticSinks(CoreLogService logService)
    {
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

        // 全局命令诊断路由到日志文件
        LogSink = message => logService.AddLog(message);

        // SDL 事件循环在后台线程执行处理器，未捕获异常会终止整个进程，接到日志兜底
        EasyCon.SDLInput.SdlEventLoop.HandlerException = ex =>
            logService.AddLog($"[SDL事件] 处理器异常: {ex.Message}");

        // 库层（Capture/Script 等）诊断转发到日志文件
        EasyCon.Core.Logging.CoreLog.Sink = message => logService.AddLog(message);
    }

    /// <summary>
    /// 注册全部子窗口工厂（DataContext 装配自 WindowService 迁来，捕获 App 作用域依赖）；
    /// 必须先于 MainWindow 构造，保证任何窗口展示路径都有工厂可用。
    /// </summary>
    private static void RegisterWindows(WindowService windowService, DeviceService deviceService, CoreLogService logService, IDialogService dialogService)
    {
        windowService.Register(WindowKeys.EspConfig, () => new ESPConfigWindow
        {
            DataContext = new ESPConfigViewModel(deviceService, logService, dialogService)
        });
        windowService.Register(WindowKeys.AlertConfig, () => new AlertConfigWindow());
        windowService.Register(WindowKeys.ModelsConfig, () => new ModelsConfigWindow());
        windowService.Register(WindowKeys.McpConfig, () => new McpConfigWindow());
        windowService.Register(WindowKeys.KeyMapping, () => new KeyMappingWindow { DataContext = new KeyMappingViewModel() });
        windowService.Register(WindowKeys.ScriptSyntax, WindowService.BuildScriptSyntaxWindow);
    }

    /// <summary>MCP Server 与 Flow 服务：GUI 启动即后台监听的两个 loopback 端点。</summary>
    private static void StartLoopbackServices(MainWindowViewModel mainWindowViewModel, DeviceService deviceService, CaptureService captureService)
    {
        // MCP Server：GUI 启动即后台监听 loopback /mcp，任意外部 agent 可驱动
        // （端口可用环境变量 EC_MCP_PORT 覆盖；设为 0 关闭）。危险工具不导出（fail-closed）。
        StartMcpServer(mainWindowViewModel);

        // Flow 服务：与 MCP 并排的第二个 loopback 端点（前端画布/外部工具的 HTTP 后端），
        // 同时把 FlowServiceTools 注册进 GUI 的 agent 工具注册中心（agent 与画布能力对等）。
        // 设备经桥接入监控页实例：画布/agent 的 flow_* 工具与 GUI 操作同一实例。
        StartFlowService(deviceService, captureService, mainWindowViewModel);
    }

    /// <summary>关窗清理：desktop.Exit 单一清理入口，服务释放统一在此（带防御）。</summary>
    private static void WireExitCleanup(IClassicDesktopStyleApplicationLifetime desktop, IControllerService controllerService, CaptureService captureService, DeviceService deviceService, IAudioCaptureSource audioSource, AudioHistoryBuffer audioHistory, AlertService alertService, CoreLogService logService, EasyCon.Core.Maa.MaaHostSession maaHost)
    {
        desktop.Exit += (_, _) =>
        {
            try { if (controllerService is IDisposable cd) cd.Dispose(); } catch { }
            try { alertService.Dispose(); } catch { }
            try { captureService.Dispose(); } catch { }
            try { audioHistory.Dispose(); } catch { }
            try { audioSource.Dispose(); } catch { }
            try { deviceService.Dispose(); } catch { }
#pragma warning disable VSTHRD002 // Exit 事件为同步签名：只能限时(2s)同步等待异步服务收尾
            try { _mcpHttpServer?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2)); } catch { }
            try { _flowHttp?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2)); } catch { }
#pragma warning restore VSTHRD002
            try { _flowState?.Dispose(); } catch { }
            try { maaHost.Dispose(); } catch { }
            logService.Dispose();
            EasyCon.Core.Logging.CoreLog.Sink = null;
        };
    }

    private static EasyCon.Core.LLM.Agent.Mcp.McpHttpServer? _mcpHttpServer;
    private static EasyCon.Core.Flow.FlowServiceState? _flowState;
    private static EasyCon.Core.Flow.FlowServiceHttp? _flowHttp;

    /// <summary>
    /// 虚拟手柄适配器：把"脚本是否运行中"桥接给 VPad 覆盖层。原为 ControllerService
    /// 私有嵌套类，随 VPadService 改为组合根注入而上移到 App，保持 Services 层零 VPad 引用。
    /// </summary>
    private sealed class VPadControllerAdapter(IScriptService scriptService) : IControllerAdapter
    {
        public bool IsRunning() => scriptService.IsRunning;
        public Color CurrentLight => Colors.White;
    }

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
    private static void StartFlowService(DeviceService deviceService, CaptureService captureService, MainWindowViewModel mainWindowViewModel)
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
            Controller = deviceService.GetDevice(),
            Frames = captureService.FrameHub,
            GetVideoSources = captureService.GetSourceDescriptors,
            IsVideoConnected = () => captureService.IsConnected,
            ConnectVideo = index => captureService.TryConnect(index)
                ? null
                : $"视频源打开失败: [{index}]",
#pragma warning disable VSTHRD002 // 桥接委托为同步签名：断开必须等采集循环退出（见上注释）
            DisconnectVideo = () => captureService.DisconnectAsync().GetAwaiter().GetResult(),
#pragma warning restore VSTHRD002
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
        var registry = mainWindowViewModel.AiAgent.SharedTools;
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

    private static void StartMcpServer(MainWindowViewModel mainWindowViewModel)
    {
        var port = int.TryParse(Environment.GetEnvironmentVariable("EC_MCP_PORT"), out var p) ? p : 19390;
        if (port <= 0)
        {
            EasyCon.Core.Logging.CoreLog.Info("[McpServer] EC_MCP_PORT<=0，已禁用 HTTP MCP");
            return;
        }

        var registry = mainWindowViewModel.AiAgent.SharedTools;
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