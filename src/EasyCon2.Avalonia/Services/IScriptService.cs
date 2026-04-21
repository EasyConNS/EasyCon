namespace EasyCon2.Avalonia.Services;

public interface IScriptService
{
    bool IsRunning { get; }
    event Action<bool> IsRunningChanged;
    void Run(string scriptPath);
    void Stop();
}