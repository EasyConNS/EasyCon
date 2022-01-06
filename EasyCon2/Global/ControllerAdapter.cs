using PTController;

namespace EasyCon2.Global
{
    class ControllerAdapter : IControllerAdapter
    {
        public Color CurrentLight => Color.White;

        public bool IsRunning()
        {
            return false;
        }
    }
}
