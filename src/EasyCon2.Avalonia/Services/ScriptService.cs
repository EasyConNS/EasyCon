using EasyCon.Capture;
using EasyCon.Core;
using EasyCon.Core.Runner;
using EasyCon.Script;
using EasyScript;
using OpenCvSharp;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace EasyCon2.Avalonia.Services;

public class ScriptService : IScriptService
{
    private readonly IDeviceService _deviceService;
    private readonly ICaptureService _captureService;
    private readonly ILogService _logService;
    private readonly EasyRunner _runner = new();
    private CancellationTokenSource? _cts;

    public bool IsRunning { get; private set; }
    public bool HasKeyAction => _runner.HasKeyAction;
    public event Action<bool> IsRunningChanged;

    public ScriptService(IDeviceService deviceService, ICaptureService captureService, ILogService logService)
    {
        _deviceService = deviceService;
        _captureService = captureService;
        _logService = logService;
    }

    public Task<bool> CompileAsync(string scriptText, string? fileName)
    {
        _logService.AddLog("开始编译...");

        try
        {
            var diag = _runner.Init(scriptText, []);
            if (diag.HasErrors())
            {
                var first = diag.First(d => d.IsError);
                _logService.AddLog($"行 {first.Location.StartLine + 1}: {first.Message}");
                return Task.FromResult(false);
            }

            _logService.AddLog("编译完成");
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logService.AddLog($"编译异常: {ex.Message}");
            return Task.FromResult(false);
        }
    }

    public string GetFormattedCode()
    {
        var formattedCode = _runner.ToCode().Trim();
        formattedCode = Regex.Replace(formattedCode, ",(?! )", ", ");
        return formattedCode;
    }

    public Task<byte[]> Build(bool autoRun)
    {
        try
        {
            var bytes = _runner.Assemble(autoRun);
            return Task.FromResult(bytes);
        }
        catch (Exception ex)
        {
            _logService.AddLog($"编译失败: {ex.Message}");
            return Task.FromResult(Array.Empty<byte>());
        }
    }

    public void Run(string scriptPath)
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        IsRunning = true;
        IsRunningChanged?.Invoke(true);
        _logService.AddLog($"开始运行脚本: {Path.GetFileName(scriptPath)}");

        Task.Run(() =>
        {
            try
            {
                var scriptBasePath = Path.GetDirectoryName(scriptPath) ?? "";
                scriptBasePath = Path.GetFullPath(scriptBasePath);
                var (label, total, repeat) = ECCore.LoadImgLabels(scriptBasePath, AppDomain.CurrentDomain.BaseDirectory);

                var diag = _runner.Load(scriptPath, [.. label.Select(il => il.name)]);

                if (diag.HasErrors())
                {
                    foreach (var d in diag)
                        _logService.AddLog($"编译失败: {d.Message} (行{d.Location.StartLine + 1})");
                    return;
                }

                if (_runner.HasKeyAction && !_deviceService.IsConnected)
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

                if (_runner.NeedILLoad && !_captureService.IsConnected)
                {
                    _logService.AddLog("错误: 脚本需要连接视频源");
                    return;
                }

                ICGamePad? pad = null;
                if (_runner.HasKeyAction)
                    pad = new GamePadAdapter(_deviceService.GetDevice());

                _captureService.SetCaptureProperties(1920, 1080);

                var labelDict = label.ToDictionary(il => il.name);
                ImmutableHashSet<string>? labelNames = [.. labelDict.Keys];

                FrameDelegate? frameDelegate = (x, y, w, h) =>
                {
                    using var mat = _captureService.GetMatFrame() ?? throw new Exception("采集卡未连接");
                    if (mat.Empty()) return null;
                    if (x >= 0 && y >= 0 && w >= 0 && h >= 0)
                    {
                        x = Math.Clamp(x, 0, mat.Width);
                        y = Math.Clamp(y, 0, mat.Height);
                        w = Math.Clamp(w, 0, mat.Width - x);
                        h = Math.Clamp(h, 0, mat.Height - y);

                        using var roi = new Mat(mat, new Rect(x, y, w, h));
                        if (w == 0 || h == 0) return null;
                        return Convert.ToBase64String(roi.ToPngBytes());
                    }
                    return Convert.ToBase64String(mat.ToPngBytes());
                };

                LabelMatchDelegate? labelMatchDelegate = lblName =>
                {
                    if (!labelDict.TryGetValue(lblName, out var il)) return 0;
                    using var mat = _captureService.GetMatFrame() ?? throw new Exception("采集卡未连接");
                    il.Search(mat, out var md, AppDomain.CurrentDomain.BaseDirectory + "Tessdata");
                    return (int)md;
                };

                var ocrCache = new OcrEngineCache();
                var ocrInit = OcrDelegateFactory.CreateInit(ocrCache);
                var ocrConf = (Func<int>)(() => ocrCache.LastConfidence);
                var fallbackDataPath = AppDomain.CurrentDomain.BaseDirectory + "Tessdata";
                var ocrDelegate = OcrDelegateFactory.Create(() => _captureService.GetMatFrame(), ocrCache, fallbackDataPath);

                _runner.Run(_logService, pad, ocrDelegate, ocrInit, ocrConf, frameDelegate, MatExtensions.CropBase64, labelMatchDelegate, labelNames, token);
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
                IsRunning = false;
                IsRunningChanged?.Invoke(false);
            }
        }, token);
    }

    public void RunFromContent(string content)
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        IsRunning = true;
        IsRunningChanged?.Invoke(true);
        _logService.AddLog("开始运行编辑区脚本");

        Task.Run(() =>
        {
            try
            {
                ImmutableHashSet<string>? labelNames = [];

                var diag = _runner.Init(content, labelNames);

                if (diag.HasErrors())
                {
                    foreach (var d in diag)
                        _logService.AddLog($"编译失败: {d.Message} (行{d.Location.StartLine + 1})");
                    return;
                }

                if (_runner.HasKeyAction && !_deviceService.IsConnected)
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

                if (_runner.NeedILLoad && !_captureService.IsConnected)
                {
                    _logService.AddLog("错误: 脚本需要连接视频源");
                    return;
                }

                ICGamePad? pad = null;
                if (_runner.HasKeyAction)
                    pad = new GamePadAdapter(_deviceService.GetDevice());

                _captureService.SetCaptureProperties(1920, 1080);

                FrameDelegate? frameDelegate = (x, y, w, h) =>
                {
                    using var mat = _captureService.GetMatFrame() ?? throw new Exception("采集卡未连接");
                    if (mat.Empty()) return null;
                    if (x >= 0 && y >= 0 && w >= 0 && h >= 0)
                    {
                        x = Math.Clamp(x, 0, mat.Width);
                        y = Math.Clamp(y, 0, mat.Height);
                        w = Math.Clamp(w, 0, mat.Width - x);
                        h = Math.Clamp(h, 0, mat.Height - y);

                        using var roi = new Mat(mat, new Rect(x, y, w, h));
                        if (w == 0 || h == 0) return null;
                        return Convert.ToBase64String(roi.ToPngBytes());
                    }
                    return Convert.ToBase64String(mat.ToPngBytes());
                };

                var ocrCache = new OcrEngineCache();
                var ocrInit = OcrDelegateFactory.CreateInit(ocrCache);
                var ocrConf = (Func<int>)(() => ocrCache.LastConfidence);
                var fallbackDataPath = AppDomain.CurrentDomain.BaseDirectory + "Tessdata";
                var ocrDelegate = OcrDelegateFactory.Create(() => _captureService.GetMatFrame(), ocrCache, fallbackDataPath);

                _runner.Run(_logService, pad, ocrDelegate, ocrInit, ocrConf, frameDelegate, MatExtensions.CropBase64, null, null, token);
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
                IsRunning = false;
                IsRunningChanged?.Invoke(false);
            }
        }, token);
    }

    public void Stop()
    {
        _cts?.Cancel();
    }
}
