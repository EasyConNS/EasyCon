using Avalonia.Media;

namespace EasyAvalonia.VPad;

public interface IControllerAdapter
{
    bool IsRunning();
    Color CurrentLight { get; }
}
