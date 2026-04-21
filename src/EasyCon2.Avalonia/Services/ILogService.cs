using EasyScript;

namespace EasyCon2.Avalonia.Services;

public interface ILogService : IOutputAdapter
{
    event Action<string>? LogAppended;
    void Clear();
    void AddLog(string message);
}