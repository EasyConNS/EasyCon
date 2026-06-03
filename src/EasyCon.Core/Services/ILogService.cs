using EasyScript;

namespace EasyCon.Core.Services;

public interface ILogService : IIoAdapter
{
    event Action<string?, string?>? LogAppended;
    void Clear();
    void AddLog(string message, string? color = null);
}