using EasyCon.Core.Config;

namespace EasyCon.Core.Services;

public interface IConfigService
{
    ConfigState Config { get; }
    KeyMappingConfig KeyMapping { get; }
    void Save();
    void UpdateKeyMapping(KeyMappingConfig keyMapping);
    void UpdateConfig(Action<ConfigState> update);
    void DispatchAlert(string message);
    Task<string?> CheckForUpdate();
}