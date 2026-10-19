using EasyCon.Capture;
using EasyCon.Capture.Audio;
using EasyCon.Core;
using EasyCon.Core.Capabilities;
using EasyCon.Core.Config;
using EasyCon.Core.Hosting;
using EasyCon.Core.Maa;
using EasyCon.Core.Script;
using EasyCon.Core.Services;
using EasyCon.Script;
using EasyScript;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace EasyCon2.Avalonia.Services;

public class ScriptService : IScriptService
{
    private readonly IDeviceService _deviceService;
    private readonly ICaptureService _captureService;
    private readonly ILogService _logService;
    private readonly IAlertService? _alertService;
    private readonly IAudioCaptureSource? _audioSource;
    private readonly IScriptEngine _engine = new EasyScriptEngine();
    private readonly HostRunCoordinator _runs;
    private readonly object _runGate = new();
    private IScriptSession? _session;
    private CancellationTokenSource? _cts;

    public bool IsRunning { get; private set; }
    public bool HasKeyAction => _session?.Info.KeyAction ?? false;
    public bool HighResolutionTiming { get; set; }

    /// <summary>
    /// 运行状态变化。注意：可能在调用线程（启动确认前）或脚本工作线程（结束时）触发，
    /// 订阅方必须自行封送到 UI 线程后再操作 UI。
    /// </summary>
    public event Action<bool> IsRunningChanged;

    /// <summary>
    /// 获取脚本运行需求（需先编译）。
    /// </summary>
    public ScriptRequirements GetRequirements()
    {
        return new ScriptRequirements(
            HasKeyAction: HasKeyAction,
            NeedImageRecognition: ScriptHardwareRequirements.RequiresLiveCapture(_session?.Info),
            DeviceConnected: _deviceService.IsConnected,
            CaptureConnected: _captureService.IsConnected
        );
    }

    public ScriptService(IDeviceService deviceService, ICaptureService captureService, ILogService logService,
        IAlertService? alertService = null, IAudioCaptureSource? audioSource = null)
    {
        _deviceService = deviceService;
        _captureService = captureService;
        _logService = logService;
        _alertService = alertService;
        _audioSource = audioSource;
        _runs = new HostRunCoordinator(deviceService.GetDevice(), captureService.FrameHub);
    }

    public async Task<bool> CompileAsync(string scriptText, string? fileName)
    {
        _logService.AddLog("开始编译...");

        try
        {
            // 编译大脚本可达数秒，放线程池执行，避免冻结调用方所在的 UI 线程
            _session = await Task.Run(() => CompileContent(scriptText, fileName).Session);
            return _session != null;
        }
        catch (Exception ex)
        {
            _session = null;
            _logService.AddLog($"编译异常: {ex.Message}");
            return false;
        }
    }

    public string GetFormattedCode()
    {
        var formattedCode = (_session?.Info.FormatCode() ?? "").Trim();
        formattedCode = Regex.Replace(formattedCode, ",(?! )", ", ");
        return formattedCode;
    }

    public Task<byte[]> BuildAsync(bool autoRun)
    {
        try
        {
            // v1 Assemble 已随 IRunner 移除（P2）；ECX 产物请用 CLI compile
            throw new NotImplementedException("v1 Assemble 已移除；请用 CLI compile 产出 .ecx");
        }
        catch (Exception ex)
        {
            _logService.AddLog($"编译失败: {ex.Message}");
            return Task.FromResult(Array.Empty<byte>());
        }
    }

    public void Run(string scriptPath, string[]? args = null)
    {
        _logService.AddLog($"开始运行脚本: {Path.GetFileName(scriptPath)}");
        ExecuteScript(() =>
        {
            string fullPath = Path.GetFullPath(scriptPath);
            LabelDiscovery labels = LoadScriptLabels(fullPath);
            IScriptSession? session = CompileCore(
                () => _engine.LoadFile(fullPath, Options([.. labels.Selected.Select(label => label.Name)])), fullPath);
            return (session, labels);
        }, args, scriptPath);
    }

    public void RunFromContent(string content, string[]? args = null, string? fileName = null)
    {
        _logService.AddLog("===开始运行脚本===");
        ExecuteScript(() => CompileContent(content, fileName), args, fileName);
    }

    public void Stop()
    {
        lock (_runGate)
        {
            _cts?.Cancel();
        }

        // 取消由运行令牌直接到达 Maa pulse；完成清理由统一协调器等待写出。
    }

    // ── 私有方法 ────────────────────────────────

    // 编译档位与能力装配都走组合根（EasyCon.Core.Hosting）：
    // 宿主不再手写 CompileOptions / CapabilitySet 字面量，避免与 CLI 漂移。
    static ScriptHostOptions Options(ImmutableHashSet<string> extVars) => new()
    {
        Compile = ScriptCompileProfiles.Interactive(extVars),
    };

    /// <summary>编译并落诊断日志；出错返回 null。</summary>
    IScriptSession? CompileCore(Func<IScriptSession> compile, string? sourcePath = null)
    {
        var session = compile();
        ImmutableArray<Diagnostic> diag = session.Info.Diagnostics;
        if (diag.HasErrors())
        {
            var errors = diag.Where(d => d.IsError)
                .DistinctBy(d => (d.FileName, d.Location.StartLine, d.Message))
                .Take(8);
            foreach (Diagnostic error in errors)
            {
                string location = FormatDiagnosticLocation(error, sourcePath);
                _logService.AddLog($"{location}: {error.Message}");
            }
            return null;
        }

        if (session.Info.Image == null)
        {
            _logService.AddLog("编译错误: 编译未生成可运行镜像");
            return null;
        }

        _logService.AddLog("编译完成");
        return session;
    }

    (IScriptSession? Session, LabelDiscovery? Labels) CompileContent(string content, string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            IScriptSession? inMemorySession = CompileCore(() => _engine.FromSource(content, Options([])));
            return (inMemorySession, null);
        }

        string fullPath = Path.GetFullPath(fileName);
        LabelDiscovery labels = LoadScriptLabels(fullPath);
        IScriptSession? session = CompileCore(
            () => _engine.FromSource(content, fullPath, Options([.. labels.Selected.Select(label => label.Name)])), fullPath);
        return (session, labels);
    }

    static LabelDiscovery LoadScriptLabels(string scriptPath)
    {
        string scriptBasePath = Path.GetDirectoryName(Path.GetFullPath(scriptPath)) ?? "";
        return LegacyLabelCatalog.FromParents(scriptBasePath, AppPaths.DataDir);
    }

    static string FormatDiagnosticLocation(Diagnostic diagnostic, string? sourcePath)
    {
        int line = diagnostic.Location.StartLine + 1;
        if (string.IsNullOrWhiteSpace(diagnostic.FileName))
            return $"行 {line}";

        string fileName = diagnostic.FileName;
        if (!string.IsNullOrWhiteSpace(sourcePath))
        {
            string baseDirectory = Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? "";
            if (Path.IsPathRooted(fileName))
                fileName = Path.GetRelativePath(baseDirectory, Path.GetFullPath(fileName));
        }
        return $"{fileName.Replace('\\', '/')}:{line}";
    }

    /// <summary>
    /// 脚本执行主流程：编译 → 检查需求 → 连接设备 → 能力装配 → 运行。
    /// </summary>
    /// <param name="compile">编译回调，返回 (会话（null=编译失败）, 标签匹配委托)</param>
    private void ExecuteScript(Func<(IScriptSession? Session, LabelDiscovery? Labels)> compile, string[]? args, string? sourcePath = null)
    {
        CancellationToken token;
        lock (_runGate)
        {
            if (IsRunning)
            {
                _logService.AddLog("脚本已在运行中，忽略本次启动请求");
                return;
            }

            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            token = _cts.Token;
            IsRunning = true;
        }

        IsRunningChanged?.Invoke(true);

        // 不把 token 传给 Task.Run：token 预先取消时任务不会调度，委托体（含 finally
        // 的状态复位）整体不执行，IsRunning 将永久卡死。取消只经 session.Run(token) 生效。
        Task.Run(() =>
        {
            try
            {
                var (session, labels) = compile();
                _session = session;
                if (session == null)
                    return;

                token.ThrowIfCancellationRequested();

                LabelGeneration? generation = null;
                IReadOnlyList<string> requiredLabels = ScriptHardwareRequirements.ReferencedLabels(session.Info);
                if (requiredLabels.Count != 0)
                {
                    string project = sourcePath == null ? AppPaths.DataDir : Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
                    string? ocrPath = Environment.GetEnvironmentVariable("EASYCON_MAA_OCR_PROFILE");
                    LabelConversionResult conversion = MaaLabelConverter.Build(labels!, MaaLabelConverter.CacheRoot(project), LegacyCompatibilityProfile.LegacyGui,
                        ocrProfile: string.IsNullOrWhiteSpace(ocrPath) ? null : MaaOcrProfile.Load(ocrPath));
                    foreach (LabelDiagnostic diagnostic in conversion.Report.Diagnostics)
                        _logService.AddLog($"标签 {diagnostic.Code}: {diagnostic.Label}: {diagnostic.Message}");
                    generation = conversion.Generation ?? throw new InvalidDataException("标签资源转换未发布");
                    generation.Preflight(requiredLabels);
                }

                // 运行所需能力必须来自本次编译结果；CompileAsync 可能并发更新公开的编辑器会话。
                bool hasKeyAction = session.Info.KeyAction;
                bool needImageRecognition = ScriptHardwareRequirements.RequiresLiveCapture(session.Info);
                ScriptRequirements requirements = new(
                    HasKeyAction: hasKeyAction,
                    NeedImageRecognition: needImageRecognition,
                    DeviceConnected: _deviceService.IsConnected,
                    CaptureConnected: _captureService.IsConnected);
                if (!requirements.CanRun)
                {
                    var reasons = requirements.GetBlockReasons();
                    foreach (var reason in reasons)
                        _logService.AddLog($"❌ {reason}");
                    return;
                }

                // 尝试自动连接单片机
                if (hasKeyAction && !_deviceService.IsConnected)
                {
                    _logService.AddLog("脚本需要单片机，尝试自动连接...");
                    var port = _deviceService.AutoConnect();
                    if (port == null)
                    {
                        _logService.AddLog("错误: 自动连接单片机失败");
                        return;
                    }
                    _logService.AddLog($"自动连接成功: {port}");
                }

                if (needImageRecognition) _captureService.SetCaptureProperties(1920, 1080);
                _runs.Run("ecs", sourcePath, hasKeyAction, token, scope =>
                {
                    var frameDelegate = FrameDelegateFactory.CreateFrame(() => scope.AcquireFrame(_captureService.AcquireLatestFrame));
                    using MaaVisionService? vision = generation == null ? null : new(generation,
                        () => scope.AcquireFrame(_captureService.AcquireLatestFrame), scope.Token);

                    // 能力装配：唯一装配点（ScriptHostAssembler）。宿主只提供原料（pad/帧/标签委托/日志），
                    // OCR、推理、宿主环境、文件能力的默认值与释放统一由租约承担。
                    // args 经 HostEnvironment 进入 VM —— 此前 ExecuteScript 的 args 形参被静默丢弃，ARG() 恒为空。
                    using CapabilityLease lease = ScriptHostAssembler.Assemble(new ScriptHostContext
                    {
                        PadInput = hasKeyAction ? scope.CreatePad(HighResolutionTiming ? DelayType.HighResolution : DelayType.LowCPU) : null,
                        Console = new ConsoleIoAdapter(_logService, _alertService),
                        Frame = frameDelegate,
                        Roi = MatExtensions.CropBase64,
                        VisionService = vision,
                        CancellationToken = scope.Token,
                        Args = args,
                        AppDir = AppDomain.CurrentDomain.BaseDirectory,
                        AudioSource = _audioSource,
                        ScriptDirectory = string.IsNullOrWhiteSpace(sourcePath) ? null : Path.GetDirectoryName(Path.GetFullPath(sourcePath)),
                    });

                    if (hasKeyAction && lease.Capabilities.Input == null)
                        throw new InvalidOperationException("脚本包含按键操作，但运行能力中未装配输入设备");

                    session.Run(scope.Token, lease.Capabilities);
                }, requiresCapture: needImageRecognition, compatibilityProfile: nameof(LegacyCompatibilityProfile.LegacyGui), resourceGeneration: generation?.Id);
                _logService.AddLog("脚本运行完成");
            }
            catch (OperationCanceledException)
            {
                _logService.AddLog("脚本已终止");
            }
            catch (ScriptException ex)
            {
                _logService.AddLog($"运行出错: {ex.Message} (行{ex.Address})");
            }
            catch (InvalidOperationException ex)
            {
                _logService.AddLog($"运行错误: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logService.AddLog($"意外错误: {ex.Message}");
            }
            finally
            {
                // OCR/推理原生资源的释放已由 CapabilityLease 在 try 作用域内完成，
                // 此处只收尾设备状态与运行标志。

                lock (_runGate)
                {
                    IsRunning = false;
                }
                IsRunningChanged?.Invoke(false);
            }
        });
    }
}