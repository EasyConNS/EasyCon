namespace EC.Capture;

public sealed class ECCapture
{
    public static List<string> GetCaptureCamera() => getCamera();

#if true
    private static List<string> getCamera() => DirectShow.GetFiltes(DirectShow.DsGuid.CLSID_VideoInputDeviceCategory);
#endif
}
