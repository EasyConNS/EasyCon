using EasyScript;

namespace EC.Avalonia.Services;

public interface ILogService : IOutputAdapter
{
    event Action<string>? LogAppended;
    void Clear();
    void AddLog(string message);
}
