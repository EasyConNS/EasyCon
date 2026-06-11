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
}
