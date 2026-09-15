using EasyCon.Capture;
using EasyCon.Core;
using EasyCon.Core.Capabilities;
using EasyCon.Core.Script;
using EasyCon.Core.Services;
using EasyCon.Script;
using EasyScript;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace EasyCon2.Avalonia.Core.Services;

public class ScriptService : IScriptService
{
    private readonly ILogService _logService;
    private readonly IDeviceService _deviceService;
    private readonly IScriptEngine _engine = new EasyScriptEngine();
    private IScriptSession? _session;
    private FrameDelegate? _frameDelegate;
    private LabelMatchDelegate? _labelMatchDelegate;
    private ImmutableHashSet<string>? _labelNames;
    private CancellationTokenSource _cts = new();
    private bool _scriptCompiling;
    private bool _scriptRunning;
    private DateTime _startTime = DateTime.MinValue;

    public bool IsRunning => _scriptRunning;
    public bool IsCompiling => _scriptCompiling;
    public event Action<bool>? IsRunningChanged;
    public event Action<string, string>? LogPrint;

    public DateTime StartTime => _startTime;

    public ScriptService(ILogService logService, IDeviceService deviceService)
    {
        _logService = logService;
        _deviceService = deviceService;
    }

    public void SetFrameProviders(FrameDelegate? frame, LabelMatchDelegate? labelMatch, ImmutableHashSet<string>? names)
    {
        _frameDelegate = frame;
        _labelMatchDelegate = labelMatch;
        _labelNames = names;
    }

    public async Task<bool> Compile(string scriptText, string? fileName)
    {
        _logService.AddLog("开始编译...");
        _scriptCompiling = true;

        try
        {
            var options = new ScriptHostOptions
            {
                Compile = new CompileOptions { ExtVars = _labelNames ?? [], UseDiskCache = false },
            };
            _session = fileName == null
                ? _engine.FromSource(scriptText, options)
                : _engine.LoadFile(fileName, options);
            ImmutableArray<Diagnostic> diag = _session.Info.Diagnostics;
            if (diag.HasErrors())
            {
                var d1 = diag.Where(d => d.IsError).First();
                _logService.AddLog($"行 {d1!.Location.StartLine + 1}：{d1!.Message}");
                return false;
            }
            _logService.AddLog("编译完成");
            return true;
        }
        catch (Exception ex)
        {
            _logService.AddLog($"异常{ex.Message}");
            return false;
        }
        finally
        {
            _scriptCompiling = false;
        }
    }

    public void Run()
    {
        if (_session == null)
            return;
        _vpadDeactivate?.Invoke();
        _logService.AddLog("开始运行");

        _scriptRunning = true;
        IsRunningChanged?.Invoke(true);

        _cts?.Cancel();
        _cts = new();
        var session = _session;
        Task.Run(() =>
        {
            _startTime = DateTime.Now;
            LogPrint?.Invoke("-- 开始运行 --", "Lime");
            try
            {
                // 能力装配（P1/P6）：帧/ROI/标签/OCR/推理经服务接口注入
                var capabilities = new CapabilitySet
                {
                    Input = new PadInputAdapter(_deviceService.CreateGamePadAdapter()),
                    Console = new ConsoleIoAdapter(_logService),
                    Capture = _frameDelegate != null ? new DelegateCaptureSource(_frameDelegate) : null,
                    Vision = new DelegateVisionService(MatExtensions.CropBase64, _labelMatchDelegate),
                    Ocr = new TesseractOcrService(new OcrEngineCache
                    {
                        DefaultDataPath = AppDomain.CurrentDomain.BaseDirectory + "Tessdata",
                    }),
                    Inference = new DnnInference(),
                };
                session.Run(_cts.Token, capabilities);
                LogPrint?.Invoke("-- 运行结束 --", "Lime");
                _logService.AddLog("运行结束");
            }
            catch (OperationCanceledException)
            {
                LogPrint?.Invoke("-- 运行终止 --", "Orange");
                _logService.AddLog("运行终止");
                Debug.WriteLine("[Beep]");
            }
            catch (ScriptException ex)
            {
                LogPrint?.Invoke($"[L{ex.Address}]：{ex.Message}", "OrangeRed");
                LogPrint?.Invoke("-- 运行出错 --", "OrangeRed");
                _logService.AddLog("运行出错");
                System.Diagnostics.Debug.WriteLine("[Hand]");
            }
            catch (Exception exx)
            {
                LogPrint?.Invoke(exx.Message, "OrangeRed");
                LogPrint?.Invoke("-- 运行出错 --", "OrangeRed");
                _logService.AddLog("运行出错");
                System.Diagnostics.Debug.WriteLine("[Hand]");
            }
            finally
            {
                _deviceService.Reset();
                _startTime = DateTime.MinValue;
                _scriptRunning = false;
                IsRunningChanged?.Invoke(false);
            }
        }, _cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _scriptRunning = false;
        _logService.AddLog("运行被终止");
        System.Diagnostics.Debug.WriteLine("[Beep]");
    }

    public string GetFormattedCode()
    {
        var formattedCode = (_session?.Info.FormatCode() ?? "").Trim();
        formattedCode = Regex.Replace(formattedCode, ",(?! )", ", ");
        return formattedCode;
    }

    public async Task<byte[]> Build(bool autoRun)
    {
        // v1 Assemble 已随 IRunner 移除（P2）；ECX 产物请用 CLI compile
        await Task.CompletedTask;
        throw new NotImplementedException("v1 Assemble 已移除；请用 CLI compile 产出 .ecx");
    }

    private Action? _vpadDeactivate;
    public void SetVPadDeactivate(Action deactivate) => _vpadDeactivate = deactivate;
}