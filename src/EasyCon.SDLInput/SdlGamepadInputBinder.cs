using EasyCon.Core.Config;
using EasyCon.Core.Input;
using EasyDevice;
using SDL;

namespace EasyCon.SDLInput;

public sealed unsafe class SdlGamepadInputBinder : IInputBinder, IDisposable
{
    private readonly SdlEventLoop _eventLoop;
    private readonly SdlGamepadDetector _detector;
    private readonly NintendoSwitch _switch;
    private readonly int _gamepadInstanceId;
    private bool _enabled;
    private bool _started;

    public SdlGamepadInputBinder(SdlEventLoop eventLoop, SdlGamepadDetector detector, NintendoSwitch @switch, int gamepadInstanceId)
    {
        _eventLoop = eventLoop;
        _detector = detector;
        _switch = @switch;
        _gamepadInstanceId = gamepadInstanceId;
    }

    public void Start()
    {
        if (_started) return;
        _started = true;
        _enabled = true;
    }

    public void Stop()
    {
        _started = false;
        _enabled = false;
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
    }

    public void UpdateKeyMapping(KeyMappingConfig mapping) { }

    public void RegisterEscapeKey(Func<bool> keydown, Func<bool> keyup) { }

    public void Poll()
    {
        if (!_started || !_enabled) return;

        var gp = _detector.GetGamepad(_gamepadInstanceId);
        if (gp == null) return;

        var report = SdlGamepadMapper.Map(gp);
        _switch.ApplyReport(report);
    }

    public void Dispose()
    {
        Stop();
    }
}