using ECDevice;

namespace EasyCon2.Script
{
    public interface ICGamePad
    {
        void ClickButtons(NintendoSwitch.ECKey key, int duration);
        void PressButtons(NintendoSwitch.ECKey key);
        void ReleaseButtons(NintendoSwitch.ECKey key);
        /* TODO
        void SetHat();
        void SetLeftStick();
        void SetRightStick();
        */
    }
}
