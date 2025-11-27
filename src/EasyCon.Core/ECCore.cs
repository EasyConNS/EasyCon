using EasyCapture;

namespace EasyCon.Core;

public partial class ECCore
{
    public static List<string> GetCaptureSources() => ECCapture.GetCaptureCamera();

    public static List<string> GetDeviceNames() => EasyDevice.ECDevice.GetPortNames();
}
