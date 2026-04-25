using EasyCon.Core.Config;
using EasyDevice;

namespace EasyCon.Core.Input;

public interface IInputBinder
{
    void Start();
    void Stop();
    void SetEnabled(bool enabled);
    void UpdateKeyMapping(KeyMappingConfig mapping);
    void RegisterEscapeKey(Func<bool> keydown, Func<bool> keyup);
}