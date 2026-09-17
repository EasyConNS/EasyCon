using EasyScript;

namespace EasyCon.Core.Capabilities;

/// <summary>
/// L1 键鼠摇杆能力（收编 <see cref="ICGamePad"/> 面）：
/// WAIT/KEY/KEYST/STICK/STICKC/AMIIBO 域指令的宿主落点。
/// </summary>
public interface IPadInput
{
    void ClickButtons(GamePadKey key, int duration, CancellationToken token);

    void PressButtons(GamePadKey key);

    void ReleaseButtons(GamePadKey key);

    void ClickStick(GamePadKey key, byte x, byte y, int duration, CancellationToken token);

    void SetStick(GamePadKey key, byte x, byte y);

    void ChangeAmiibo(uint index);
}