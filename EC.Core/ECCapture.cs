using GitHub.secile.Video;

namespace EC.Core;

public partial class ECCore
{
    public static List<string> GetCaptureCamera() => UsbCamera.FindDevices().ToList();
}
