using Avalonia.Media;

namespace EasyCon2.Avalonia.VPad;

public interface IControllerAdapter
{
    bool IsRunning();
    Color CurrentLight { get; }
}