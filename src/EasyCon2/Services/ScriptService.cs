using EasyCon.Capture;
using EasyCon.Core;
using EasyCon.Core.Runner;
using EasyCon.Script;
using EasyScript;
using System.Collections.Immutable;
using System.Drawing;

namespace EasyCon2.Services;

public class ScriptService
{
    private readonly EasyRunner _runner = new();
    private FrameDelegate? _frameDelegate;
    private LabelMatchDelegate? _labelMatchDelegate;
    private ImmutableHashSet<string>? _labelNames;
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
        string code, string? fileName, FrameDelegate? frameDelegate, LabelMatchDelegate? labelMatch, ImmutableHashSet<string>? labelNames)
    {
        try
        {
            _frameDelegate = frameDelegate;
            _labelMatchDelegate = labelMatch;
            _labelNames = labelNames;
            var extVarNames = labelNames ?? [];
            ImmutableArray<Diagnostic> diag = fileName == null
                ? _runner.Init(code, extVarNames)
                : _runner.Load(fileName, extVarNames);
            if (diag.Any(d => d.IsError))
            {
                var err = diag.First(d => d.IsError);
                return (false, $"行 {err.Location.StartLine + 1}", err.Message + $"\n在({err.FileName})");
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
        string code, string? fileName, FrameDelegate? frameDelegate, LabelMatchDelegate? labelMatch, ImmutableHashSet<string>? labelNames)
    {
        var (success, errorLine, errorMessage) = Compile(code, fileName, frameDelegate, labelMatch, labelNames);
        if (!success)
            return (false, null, errorLine, errorMessage);

        try
        {
            var formatted = _runner.ToCode().Trim();
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
    public void Run(IIoAdapter ioAdapter, ICGamePad pad, OcrDelegate? ocr, OcrInitDelegate? ocrInit, Func<int> ocrConf)
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
                _runner.Run(ioAdapter, pad, ocr, ocrInit, ocrConf, _frameDelegate, MatExtensions.CropBase64, _labelMatchDelegate, _labelNames, _cts.Token);
                LogOutput?.Invoke("-- 运行结束 --", Color.Lime);
            }
            catch (OperationCanceledException)
            {
                LogOutput?.Invoke("-- 运行终止 --", Color.Orange);
            }
            catch (ScriptException ex)
            {
                LogOutput?.Invoke($"[L{ex.Address}]：{ex.Message}", Color.OrangeRed);
                LogOutput?.Invoke("-- 运行出错 --", Color.OrangeRed);
            }
            catch (Exception ex)
            {
                File.WriteAllText("trace.log", ex.StackTrace + "\n" + ex.InnerException?.StackTrace);
                LogOutput?.Invoke(ex.Message, Color.OrangeRed);
                LogOutput?.Invoke("-- 运行出错 --", Color.OrangeRed);
            }
            finally
            {
                pad.Reset();
                StatusChanged?.Invoke("运行结束");
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
    /// 编译为字节码（供烧录和固件生成使用，需先 Compile）
    /// </summary>
    public byte[]? Assemble(bool autoRun = true)
    {
        try
        {
            return _runner.Assemble(autoRun);
        }
        catch
        {
            return null;
        }
    }

    public bool HasKeyAction => _runner.HasKeyAction;
}