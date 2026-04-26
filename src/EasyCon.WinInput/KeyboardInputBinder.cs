using EasyCon.Core.Config;
using EasyCon.Core.Input;
using EasyDevice;
using System.Runtime.Versioning;

namespace EasyCon.WinInput;

[SupportedOSPlatform("windows")]
public class KeyboardInputBinder : IInputBinder
{
    private readonly KeyBinder _keyBinder = new();
    private readonly NintendoSwitch _ns;
    private KeyMappingConfig _mapping;

    public KeyboardInputBinder(NintendoSwitch ns, KeyMappingConfig mapping)
    {
        _ns = ns;
        _mapping = mapping;
    }

    public void Start() => _keyBinder.RegisterAllKeys(_mapping, _ns);
    public void Stop() => _keyBinder.UnregisterAllKeys();
    public void SetEnabled(bool enabled) => _keyBinder.ControllerEnabled = enabled;
    public void UpdateKeyMapping(KeyMappingConfig mapping)
    {
        _mapping = mapping;
        _keyBinder.RegisterAllKeys(_mapping, _ns);
    }

    public void RegisterEscapeKey(Func<bool> keydown, Func<bool> keyup)
        => _keyBinder.RegisterEscapeKey(keydown, keyup);
}