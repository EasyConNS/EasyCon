using EC.Capture;
using EC.Device;

namespace EC.Core;

public partial class ECCore
{
    public static List<string> GetCaptureSources() => ECCapture.GetCaptureCamera();

    public static List<string> GetDeviceNames() => ECDevice.GetPortNames();
}
