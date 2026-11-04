namespace EasyCon2.Avalonia.Services;

public interface IScriptService
{
    bool IsRunning { get; }
    bool HasKeyAction { get; }
    bool HighResolutionTiming { get; set; }
    event Action<bool> IsRunningChanged;
    Task<bool> CompileAsync(string scriptText, string? fileName);
    string GetFormattedCode();
    /// <summary>按 Distributable 编译档导出 ECX；stripDebug 控制是否省略调试段。</summary>
    Task<byte[]> BuildAsync(bool stripDebug);
    void Run(string scriptPath, string[]? args = null);
    void RunFromContent(string content, string[]? args = null, string? fileName = null);
    void Stop();

    /// <summary>
    /// 获取脚本运行需求（需先编译）。
    /// </summary>
    ScriptRequirements GetRequirements();
}