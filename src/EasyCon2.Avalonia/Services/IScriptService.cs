namespace EasyCon2.Avalonia.Services;

public interface IScriptService
{
    bool IsRunning { get; }
    event Action<bool> IsRunningChanged;
    Task<bool> CompileAsync(string scriptText, string? fileName);
    string GetFormattedCode();
    void Run(string scriptPath);
    void RunFromContent(string content);
    void Stop();
}
