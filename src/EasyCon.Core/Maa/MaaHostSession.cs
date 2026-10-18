#nullable enable
namespace EasyCon.Core.Maa;

/// <summary>应用级组合根。采集与输入独立连接，GUI/CLI/Flow 共用相同宿主库。</summary>
public sealed class MaaHostSession : IDisposable
{
    public FrameHub Frames { get; } = new();
    public IControllerSession Controller { get; }
    public HostRunCoordinator Runs { get; }

    public MaaHostSession()
    {
        Controller = new MaaControllerSession();
        Runs = new HostRunCoordinator(Controller, Frames);
    }

    public void Dispose()
    {
        Controller.Dispose();
        Frames.Dispose();
    }
}