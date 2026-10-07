using EasyCon.Capture;
using EasyCon.Core;
using EasyCon.Core.Capabilities;
using EasyCon.Core.Config;
using EasyCon.Core.Hosting;
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
    private readonly IScriptEngine _engine = new EasyScriptEngine();
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
            NeedImageRecognition: _session?.Info.NeedIL ?? false,
            DeviceConnected: _deviceService.IsConnected,
            CaptureConnected: _captureService.IsConnected
        );
    }

    public ScriptService(IDeviceService deviceService, ICaptureService captureService, ILogService logService)
    {
        _deviceService = deviceService;
        _captureService = captureService;
        _logService = logService;
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
            Dictionary<string, ImgLabel> labelDict = LoadScriptLabels(fullPath);
            IScriptSession? session = CompileCore(
                () => _engine.LoadFile(fullPath, Options([.. labelDict.Keys])), fullPath);
            return (session, BuildLabelMatchDelegate(labelDict));
        }, args);
    }

    public void RunFromContent(string content, string[]? args = null, string? fileName = null)
    {
        _logService.AddLog("===开始运行脚本===");
        ExecuteScript(() => CompileContent(content, fileName), args);
    }

    public void Stop()
    {
        lock (_runGate)
        {
            _cts?.Cancel();
        }

        // Reset 会抢设备写循环的锁，移到后台执行，避免卡顿串口拖住 UI 线程。
        var device = _deviceService;
        _ = Task.Run(() =>
        {
            try
            {
                device.Reset();
            }
            catch (Exception ex)
            {
                // 复位失败时设备可能停在最后一次按键状态，必须留痕
                _logService.AddLog($"设备复位失败: {ex.Message}");
            }
        });
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

    (IScriptSession? Session, LabelMatchDelegate? LabelMatch) CompileContent(string content, string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            IScriptSession? inMemorySession = CompileCore(() => _engine.FromSource(content, Options([])));
            return (inMemorySession, null);
        }

        string fullPath = Path.GetFullPath(fileName);
        Dictionary<string, ImgLabel> labelDict = LoadScriptLabels(fullPath);
        IScriptSession? session = CompileCore(
            () => _engine.FromSource(content, fullPath, Options([.. labelDict.Keys])), fullPath);
        return (session, BuildLabelMatchDelegate(labelDict));
    }

    static Dictionary<string, ImgLabel> LoadScriptLabels(string scriptPath)
    {
        string scriptBasePath = Path.GetDirectoryName(Path.GetFullPath(scriptPath)) ?? "";
        var (label, total, repeat) = ECCore.LoadImgLabels(scriptBasePath, AppPaths.DataDir);
        return label.ToDictionary(il => il.name);
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

    private LabelMatchDelegate? BuildLabelMatchDelegate(Dictionary<string, ImgLabel> labelDict)
    {
        if (labelDict.Count == 0) return null;
        return lblName =>
        {
            if (!labelDict.TryGetValue(lblName, out var il)) return 0;
            using var lease = _captureService.AcquireLatestFrame() ?? throw new Exception("采集卡未连接");
            il.Search(lease.Mat, out var md, AppDomain.CurrentDomain.BaseDirectory + "Tessdata");
            return (int)md;
        };
    }

    /// <summary>
    /// 脚本执行主流程：编译 → 检查需求 → 连接设备 → 能力装配 → 运行。
    /// </summary>
    /// <param name="compile">编译回调，返回 (会话（null=编译失败）, 标签匹配委托)</param>
    private void ExecuteScript(Func<(IScriptSession? Session, LabelMatchDelegate? LabelMatch)> compile, string[]? args)
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
                var (session, labelMatch) = compile();
                _session = session;
                if (session == null)
                    return;

                token.ThrowIfCancellationRequested();

                // 运行所需能力必须来自本次编译结果；CompileAsync 可能并发更新公开的编辑器会话。
                bool hasKeyAction = session.Info.KeyAction;
                bool needImageRecognition = session.Info.NeedIL;
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

                ICGamePad? pad = null;
                if (hasKeyAction)
                    pad = new GamePadAdapter(_deviceService.GetDevice(), HighResolutionTiming);

                _captureService.SetCaptureProperties(1920, 1080);

                var frameDelegate = FrameDelegateFactory.CreateFrame(() => _captureService.AcquireLatestFrame());

                // 能力装配：唯一装配点（ScriptHostAssembler）。宿主只提供原料（pad/帧/标签委托/日志），
                // OCR、推理、宿主环境、文件能力的默认值与释放统一由租约承担。
                // args 经 HostEnvironment 进入 VM —— 此前 ExecuteScript 的 args 形参被静默丢弃，ARG() 恒为空。
                using CapabilityLease lease = ScriptHostAssembler.Assemble(new ScriptHostContext
                {
                    Pad = pad,
                    Console = new ConsoleIoAdapter(_logService),
                    Frame = frameDelegate,
                    Roi = MatExtensions.CropBase64,
                    LabelMatch = labelMatch,
                    Args = args,
                    AppDir = AppDomain.CurrentDomain.BaseDirectory,
                });

                if (hasKeyAction && lease.Capabilities.Input == null)
                    throw new InvalidOperationException("脚本包含按键操作，但运行能力中未装配输入设备");

                session.Run(token, lease.Capabilities);
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

                try
                {
                    _deviceService.Reset();
                }
                catch (Exception ex)
                {
                    // 复位失败不能拦住下面的 IsRunning 复位，否则运行按钮永久卡死
                    _logService.AddLog($"设备复位失败: {ex.Message}");
                }

                lock (_runGate)
                {
                    IsRunning = false;
                }
                IsRunningChanged?.Invoke(false);
            }
        });
    }
}