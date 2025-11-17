namespace EasyCapture;

public sealed class ECCapture
{
    public static List<string> GetCaptureCamera() => getCamera();

    private static List<string> getCamera() => DirectShow.GetFiltes(DirectShow.DsGuid.CLSID_VideoInputDeviceCategory);
}

