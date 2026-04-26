using EasyCon.Core;
using EasyCon.Core.Services;
using EasyCon.Script;
using EasyCon.Script.Syntax;
using EasyScript;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace EasyCon2.Avalonia.Core.Services;

public class ScriptService : IScriptService
{
    private readonly ILogService _logService;
    private readonly IDeviceService _deviceService;
    private readonly Scripter _program = new();
    private CancellationTokenSource _cts = new();
    private bool _scriptCompiling;
    private bool _scriptRunning;
    private DateTime _startTime = DateTime.MinValue;

    private Dictionary<string, Func<int>>? _externalGetters;

    public bool IsRunning => _scriptRunning;
    public bool IsCompiling => _scriptCompiling;
    public event Action<bool>? IsRunningChanged;
    public event Action<string, string>? LogPrint;

    public DateTime StartTime => _startTime;
    public Scripter Program => _program;

    public ScriptService(ILogService logService, IDeviceService deviceService)
    {
        _logService = logService;
        _deviceService = deviceService;
    }

    public void SetExternalGetters(Dictionary<string, Func<int>> getters)
    {
        _externalGetters = getters;
    }

    public async Task<bool> Compile(string scriptText, string? fileName)
    {
        _logService.AddLog("开始编译...");
        _scriptCompiling = true;

        try
        {
            var diag = _program.Parse(scriptText, fileName, _externalGetters ?? new());
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
        _vpadDeactivate?.Invoke();
        _logService.AddLog("开始运行");

        _scriptRunning = true;
        IsRunningChanged?.Invoke(true);

        _cts?.Cancel();
        _cts = new();
        Task.Run(() =>
        {
            _startTime = DateTime.Now;
            LogPrint?.Invoke("-- 开始运行 --", "Lime");
            try
            {
                _program.Run(_logService, _deviceService.CreateGamePadAdapter(), _cts.Token);
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

    public void Reset()
    {
        if (_scriptCompiling || _scriptRunning)
            return;
        _program.Reset();
    }

    public string GetFormattedCode()
    {
        var formattedCode = _program.ToCode().Trim();
        formattedCode = Regex.Replace(formattedCode, ",(?! )", ", ");
        return formattedCode;
    }

    public async Task<byte[]> Build(bool autoRun)
    {
        var bytes = _program.Assemble(autoRun);
        return bytes ?? [];
    }

    private Action? _vpadDeactivate;
    public void SetVPadDeactivate(Action deactivate) => _vpadDeactivate = deactivate;
}