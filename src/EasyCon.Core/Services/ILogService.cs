using EasyScript;

namespace EasyCon.Core.Services;

public interface ILogService : IOutputAdapter
{
    event Action<string?, string?>? LogAppended;
    void Clear();
    void AddLog(string message, string? color = null);
}