namespace EasyVPad;

public interface IControllerAdapter
{
    bool IsRunning();
    Color CurrentLight { get; }
}
