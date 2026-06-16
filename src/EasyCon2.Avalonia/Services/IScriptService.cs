namespace EasyCon2.Avalonia.Services;

public interface IScriptService
{
    bool IsRunning { get; }
    bool HasKeyAction { get; }
    bool HighResolutionTiming { get; set; }
    event Action<bool> IsRunningChanged;
    Task<bool> CompileAsync(string scriptText, string? fileName);
    string GetFormattedCode();
    Task<byte[]> BuildAsync(bool autoRun);
    void Run(string scriptPath);
    void RunFromContent(string content);
    void Stop();

    /// <summary>
    /// 获取脚本运行需求（需先编译）。
    /// </summary>
    ScriptRequirements GetRequirements();
}

/// <summary>
/// 脚本运行需求检查结果。
/// </summary>
public record ScriptRequirements(
    bool HasKeyAction,
    bool NeedImageRecognition,
    bool DeviceConnected,
    bool CaptureConnected
)
{
    /// <summary>是否满足所有运行需求。</summary>
    public bool CanRun => (!HasKeyAction || DeviceConnected) && (!NeedImageRecognition || CaptureConnected);

    /// <summary>返回不满足需求的原因列表。</summary>
    public List<string> GetBlockReasons()
    {
        var reasons = new List<string>();
        if (HasKeyAction && !DeviceConnected)
            reasons.Add("脚本包含按键操作，需要连接单片机");
        if (NeedImageRecognition && !CaptureConnected)
            reasons.Add("脚本包含图像识别操作，需要连接视频源");
        return reasons;
    }
}