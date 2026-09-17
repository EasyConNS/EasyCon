namespace EasyCon2.Avalonia.Core.Services;

public interface IScriptService
{
    bool IsRunning { get; }
    bool HasKeyAction { get; }
    bool HighResolutionTiming { get; set; }
    event Action<bool> IsRunningChanged;
    Task<bool> CompileAsync(string scriptText, string? fileName);
    string GetFormattedCode();
    Task<byte[]> BuildAsync(bool autoRun);
    void Run(string scriptPath, string[]? args = null);
    void RunFromContent(string content, string[]? args = null);
    void Stop();

    /// <summary>
    /// 获取脚本运行需求（需先编译）。
    /// </summary>
    ScriptRequirements GetRequirements();
}