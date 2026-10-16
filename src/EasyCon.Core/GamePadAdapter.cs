#nullable enable
using EasyCon.Core.Capabilities;
using EasyCon.Core.Maa;
using EasyScript;

namespace EasyCon.Core;

/// <summary>保留旧脚本手柄接口，全部操作委托给 Maa 输入端口。</summary>
public sealed class GamePadAdapter : ICGamePad
{
    private readonly IPadInput _pad;
    public DelayType DelayMethod { get; }

    public GamePadAdapter(IControllerSession session, bool highResolution = false, ControllerRunLease? run = null)
    {
        DelayMethod = highResolution ? DelayType.HighResolution : DelayType.LowCPU;
        _pad = session.CreatePad(DelayMethod, run);
    }

    public void ClickButtons(GamePadKey key, int duration, CancellationToken token) => _pad.ClickButtons(key, duration, token);
    public void PressButtons(GamePadKey key) => _pad.PressButtons(key);
    public void ReleaseButtons(GamePadKey key) => _pad.ReleaseButtons(key);
    public void ClickStick(GamePadKey key, byte x, byte y, int duration, CancellationToken token) => _pad.ClickStick(key, x, y, duration, token);
    public void SetStick(GamePadKey key, byte x, byte y) => _pad.SetStick(key, x, y);
    public void ChangeAmiibo(uint index) => _pad.ChangeAmiibo(index);
    public void Reset() => ((MaaPadInput)_pad).Reset();
}