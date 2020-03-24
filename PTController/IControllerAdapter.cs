using System.Drawing;

namespace PTController
{
    public interface IControllerAdapter
    {
        bool IsRunning();
        Color CurrentLight { get; }
    }
}
