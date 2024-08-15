using GitHub.secile.Video;

namespace EC.Capture;

public class ECCapture
{
    public static List<string> GetCaptureCamera() => FindDevices();
    private static List<string>FindDevices()
    {
        return DirectShow.GetFiltes(DirectShow.DsGuid.CLSID_VideoInputDeviceCategory);
    }
}
