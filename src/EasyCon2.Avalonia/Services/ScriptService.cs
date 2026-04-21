using EasyCon.Capture;
using EasyCon.Core;
using EasyCon.Core.Runner;
using EasyCon.Script;
using EasyScript;

namespace EasyCon2.Avalonia.Services;

public class ScriptService : IScriptService
{
    private readonly IDeviceService _deviceService;
    private readonly ICaptureService _captureService;
    private readonly ILogService _logService;
    private readonly EasyRunner _runner = new();
    private CancellationTokenSource? _cts;

    public bool IsRunning { get; private set; }
    public event Action<bool> IsRunningChanged;

    public ScriptService(IDeviceService deviceService, ICaptureService captureService, ILogService logService)
    {
        _deviceService = deviceService;
        _captureService = captureService;
        _logService = logService;
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

                var externalGetters = label.ToDictionary(il => il.name, il => (Func<int>)(() =>
                {
                    using var mat = _captureService.GetMatFrame() ?? throw new Exception("采集卡未连接");
                    il.Search(mat, out var md);
                    return (int)md;
                }));

                _runner.Run(_logService, pad, externalGetters, token);
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
        _logService.AddLog("正在停止脚本...");
        _cts?.Cancel();
    }
}