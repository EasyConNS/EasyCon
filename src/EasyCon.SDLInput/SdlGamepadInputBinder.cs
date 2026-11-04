using EasyCon.Core.Config;
using EasyCon.Core.Input;
using EasyCon.Core.Maa;
using EasyDevice;
using SDL;

namespace EasyCon.SDLInput;

public sealed unsafe class SdlGamepadInputBinder : IInputBinder, IDisposable
{
    private readonly SdlEventLoop _eventLoop;
    private readonly SdlGamepadDetector _detector;
    private readonly IControllerSession _switch;
    private readonly int _gamepadInstanceId;
    private bool _enabled;
    private bool _started;

    public SdlGamepadInputBinder(SdlEventLoop eventLoop, SdlGamepadDetector detector, IControllerSession @switch, int gamepadInstanceId)
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
        // 复位设备：断开/切换控制源时清掉最后一份报告（如按住的摇杆方向），
        // 否则该状态会一直残留在 Switch 上，直到下一个 binder 写入
        if (_switch.CanAcceptManualInput) _switch.Reset();
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
    }

    public void UpdateKeyMapping(KeyMappingConfig mapping) { }

    public void RegisterEscapeKey(Func<bool> keydown, Func<bool> keyup) { }

    public void Poll()
    {
        if (!_started || !_enabled || !_switch.CanAcceptManualInput) return;

        var gp = _detector.GetGamepad(_gamepadInstanceId);
        if (gp == null) return;

        var report = SdlGamepadMapper.Map(gp);
        try { _switch.ApplyReport(report); }
        catch (ControllerOperationException ex) when (ex.ErrorCode is "Busy" or "InputDisconnected") { }
    }

    public void Dispose()
    {
        Stop();
    }
}