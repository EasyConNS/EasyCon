using EasyScript;

namespace EasyCon2.Avalonia.Services;

public interface ILogService : IIoAdapter
{
    event Action<string>? LogAppended;
    void Clear();
    void AddLog(string message);
}