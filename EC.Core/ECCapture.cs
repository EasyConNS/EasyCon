using GitHub.secile.Video;

namespace EC.Core;

public partial class ECCapture
{
    public static List<string> GetCaptureCamera() => UsbCamera.FindDevices().ToList();
}
