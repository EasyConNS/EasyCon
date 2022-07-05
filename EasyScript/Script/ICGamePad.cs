using ECDevice;

namespace EasyScript;

public interface ICGamePad
{
    void ClickButtons(ECKey key, int duration);
    void PressButtons(ECKey key);
    void ReleaseButtons(ECKey key);
    /* TODO
    void SetHat();
    void SetLeftStick();
    void SetRightStick();
    */
}
