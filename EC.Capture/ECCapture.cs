namespace EC.Capture;

public partial class ECCapture
{
    public static List<string> GetCaptureCamera() => getCamera();

#if true
    private static List<string> getCamera() => GitHub.secile.Video.DirectShow.GetFiltes(GitHub.secile.Video.DirectShow.DsGuid.CLSID_VideoInputDeviceCategory);
#endif
}
