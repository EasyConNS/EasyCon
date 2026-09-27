using EasyCon.Capture;
using EasyCon.Core;
using EasyCon.Core.Capabilities;
using EasyCon.Core.Config;
using EasyCon.Core.Script;
using EasyCon.Core.Services;
using EasyCon.Script;
using EasyScript;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace EasyCon2.Avalonia.Core.Services;

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
            _session = await Task.Run(() => CompileCore(() => _engine.FromSource(scriptText, Options([]))));
            return _session != null;
        }
        catch (Exception ex)
        {
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
            var scriptBasePath = Path.GetFullPath(Path.GetDirectoryName(scriptPath) ?? "");
            var (label, total, repeat) = ECCore.LoadImgLabels(scriptBasePath, AppPaths.DataDir);
            var labelDict = label.ToDictionary(il => il.name);
            var session = CompileCore(() => _engine.LoadFile(scriptPath, Options([.. labelDict.Keys])));
            return (session, BuildLabelMatchDelegate(labelDict));
        }, args);
    }

    public void RunFromContent(string content, string[]? args = null)
    {
        _logService.AddLog("===开始运行脚本===");
        ExecuteScript(() => (_session = CompileCore(() => _engine.FromSource(content, Options([]))), null), args);
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

    static ScriptHostOptions Options(ImmutableHashSet<string> extVars) => new()
    {
        Compile = new CompileOptions { ExtVars = extVars, UseDiskCache = false },
    };

    /// <summary>编译并落诊断日志；出错返回 null。</summary>
    IScriptSession? CompileCore(Func<IScriptSession> compile)
    {
        var session = compile();
        ImmutableArray<Diagnostic> diag = session.Info.Diagnostics;
        if (diag.HasErrors())
        {
            var first = diag.First(d => d.IsError);
            _logService.AddLog($"行 {first.Location.StartLine + 1}: {first.Message}");
            return null;
        }

        _logService.AddLog("编译完成");
        return session;
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
            CapabilitySet? capabilities = null;
            try
            {
                var (session, labelMatch) = compile();
                if (session == null)
                    return;

                // 检查脚本运行需求
                var requirements = GetRequirements();
                if (!requirements.CanRun)
                {
                    var reasons = requirements.GetBlockReasons();
                    foreach (var reason in reasons)
                        _logService.AddLog($"❌ {reason}");
                    return;
                }

                // 尝试自动连接单片机
                if (HasKeyAction && !_deviceService.IsConnected)
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
                if (HasKeyAction)
                    pad = new GamePadAdapter(_deviceService.GetDevice(), HighResolutionTiming);

                _captureService.SetCaptureProperties(1920, 1080);

                var frameDelegate = FrameDelegateFactory.CreateFrame(() => _captureService.AcquireLatestFrame());

                // 能力装配（P6）：帧/ROI/标签/OCR/推理经服务接口注入
                capabilities = new CapabilitySet
                {
                    Input = pad != null ? new PadInputAdapter(pad) : null,
                    Console = new ConsoleIoAdapter(_logService),
                    Capture = new DelegateCaptureSource(frameDelegate),
                    Vision = new DelegateVisionService(MatExtensions.CropBase64, labelMatch),
                    Ocr = new TesseractOcrService(new OcrEngineCache
                    {
                        DefaultDataPath = AppDomain.CurrentDomain.BaseDirectory + "Tessdata"
                    }),
                    Inference = new DnnInference(),
                };

                session.Run(token, capabilities);
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
            catch (Exception ex)
            {
                _logService.AddLog($"意外错误: {ex.Message}");
            }
            finally
            {
                // OCR 缓存与推理引擎持有原生 Tesseract/DNN 资源，随本次运行释放
                (capabilities?.Ocr as IDisposable)?.Dispose();
                (capabilities?.Inference as IDisposable)?.Dispose();

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