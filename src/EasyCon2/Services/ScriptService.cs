using EasyCon.Core;
using EasyCon.Core.Runner;
using EasyCon.Script;
using EasyScript;
using System.Drawing;

namespace EasyCon2.Services;

public class ScriptService
{
    private readonly Scripter _scripter = new();
    private CancellationTokenSource _cts = new();

    /// <summary>脚本运行状态变化</summary>
    public event Action<bool>? RunningStateChanged;

    /// <summary>日志输出 (message, color?)</summary>
    public event Action<string, Color?>? LogOutput;

    /// <summary>状态栏消息</summary>
    public event Action<string>? StatusChanged;

    public bool IsRunning { get; private set; }

    /// <summary>
    /// 编译脚本
    /// </summary>
    public (bool success, string? errorLine, string? errorMessage) Compile(
        string code, string? fileName, Dictionary<string, Func<int>> externalGetters)
    {
        try
        {
            var diag = _scripter.Parse(code, fileName, externalGetters);
            if (diag.Any(d => d.IsError))
            {
                var err = diag.First(d => d.IsError);
                return (false, $"行 {err.Location.StartLine + 1}", err.Message);
            }
            return (true, null, null);
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    /// <summary>
    /// 格式化脚本（编译后返回格式化代码）
    /// </summary>
    public (bool success, string? formattedCode, string? errorLine, string? errorMessage) Format(
        string code, string? fileName, Dictionary<string, Func<int>> externalGetters)
    {
        var (success, errorLine, errorMessage) = Compile(code, fileName, externalGetters);
        if (!success)
            return (false, null, errorLine, errorMessage);

        try
        {
            var formatted = _scripter.ToCode().Trim();
            // 确保逗号后面有空格
            formatted = System.Text.RegularExpressions.Regex.Replace(formatted, @",(?! )", ", ");
            return (true, formatted, null, null);
        }
        catch (Exception ex)
        {
            return (false, null, null, ex.Message);
        }
    }

    /// <summary>
    /// 运行脚本（需要先 Compile 或 Format）
    /// </summary>
    public void Run(IOutputAdapter output, ICGamePad pad)
    {
        if (IsRunning) return;

        IsRunning = true;
        RunningStateChanged?.Invoke(true);
        LogOutput?.Invoke("-- 开始运行 --", Color.Lime);
        StatusChanged?.Invoke("运行中");

        _cts.Cancel();
        _cts = new();

        Task.Run(() =>
        {
            try
            {
                _scripter.Run(output, pad, _cts.Token);
                LogOutput?.Invoke("-- 运行结束 --", Color.Lime);
                StatusChanged?.Invoke("运行结束");
            }
            catch (OperationCanceledException)
            {
                LogOutput?.Invoke("-- 运行终止 --", Color.Orange);
                StatusChanged?.Invoke("运行终止");
            }
            catch (ScriptException ex)
            {
                LogOutput?.Invoke($"[L{ex.Address}]：{ex.Message}", Color.OrangeRed);
                LogOutput?.Invoke("-- 运行出错 --", Color.OrangeRed);
                StatusChanged?.Invoke("运行出错");
            }
            catch (Exception ex)
            {
                LogOutput?.Invoke(ex.Message, Color.OrangeRed);
                LogOutput?.Invoke("-- 运行出错 --", Color.OrangeRed);
                StatusChanged?.Invoke("运行出错");
            }
            finally
            {
                IsRunning = false;
                RunningStateChanged?.Invoke(false);
            }
        }, _cts.Token);
    }

    /// <summary>
    /// 停止运行
    /// </summary>
    public void Stop()
    {
        _cts.Cancel();
        StatusChanged?.Invoke("运行被终止");
    }

    /// <summary>
    /// 重置脚本引擎
    /// </summary>
    public void Reset()
    {
        if (IsRunning) return;
        _scripter.Reset();
    }

    /// <summary>
    /// 编译为字节码（供烧录和固件生成使用，需先 Compile）
    /// </summary>
    public byte[]? Assemble(bool autoRun = true)
    {
        try
        {
            return _scripter.Assemble(autoRun);
        }
        catch
        {
            return null;
        }
    }

    public bool HasKeyAction => _scripter.HasKeyAction;
}