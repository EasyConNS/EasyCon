using Avalonia.Media;

namespace EasyCon2.Avalonia.Core.VPad;

public interface IControllerAdapter
{
    bool IsRunning();
    Color CurrentLight { get; }
}
