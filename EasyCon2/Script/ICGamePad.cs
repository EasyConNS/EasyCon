using ECDevice;

namespace EasyCon2.Script
{
    public interface ICGamePad
    {
        void ClickButtons(NintendoSwitch.Key key, int duration);
        void PressButtons(NintendoSwitch.Key key);
        void ReleaseButtons(NintendoSwitch.Key key);
        /* TODO
        void SetHat();
        void SetLeftStick();
        void SetRightStick();
        */
    }
}
