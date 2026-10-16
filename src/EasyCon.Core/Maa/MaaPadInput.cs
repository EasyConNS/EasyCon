#nullable enable
using EasyCon.Core.Capabilities;
using EasyDevice;
using EasyScript;

namespace EasyCon.Core.Maa;

/// <summary>ECS 六个输入操作的 Maa 适配；延时与释放在同一次可取消 Shell 回调内。</summary>
public sealed class MaaPadInput : IPadInput
{
    private readonly MaaControllerSession _session;
    private readonly ControllerRunLease? _run;
    private readonly DelayType _timing;

    internal MaaPadInput(MaaControllerSession session, DelayType timing, ControllerRunLease? run)
    {
        _session = session;
        _run = run;
        _timing = timing;
    }

    private void Send(string op, ECKey key, int duration = 0, CancellationToken token = default)
        => _session.Send(new ControllerCommand
        {
            Op = op,
            Key = ControllerKey.FromKey(key),
            Duration = duration,
            Timing = _timing,
        }, _run, token);

    public void ClickButtons(GamePadKey key, int duration, CancellationToken token)
        => Send("input.pulse", key.ToECKey(), duration, token);
    public void PressButtons(GamePadKey key) => Send("input.down", key.ToECKey());
    public void ReleaseButtons(GamePadKey key) => Send("input.up", key.ToECKey());
    public void ClickStick(GamePadKey key, byte x, byte y, int duration, CancellationToken token)
        => Send("input.pulse", key.ToECKey(x, y), duration, token);
    public void SetStick(GamePadKey key, byte x, byte y)
        => Send(x == SwitchStick.STICK_CENTER && y == SwitchStick.STICK_CENTER ? "input.up" : "input.down",
            key.ToECKey(x, y));
    public void ChangeAmiibo(uint index)
        => _session.Send(new ControllerCommand { Op = "amiibo.index", Index = (byte)(index & 0x0F) }, _run);
    internal void Reset() => _session.Send(new ControllerCommand { Op = "input.reset" }, _run);
}