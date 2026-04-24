namespace EasyCon.Core.Services;

public interface IScriptService
{
    bool IsRunning { get; }
    bool IsCompiling { get; }
    DateTime StartTime { get; }
    event Action<bool>? IsRunningChanged;
    event Action<string, string>? LogPrint;
    Task<bool> Compile(string scriptText, string? fileName);
    void Run();
    void Stop();
    void Reset();
    string GetFormattedCode();
    Task<byte[]> Build(bool autoRun);
}