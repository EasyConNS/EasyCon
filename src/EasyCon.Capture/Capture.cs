using OpenCvSharp;

namespace EasyCapture;

public sealed class ECCapture
{
    public static IEnumerable<string> GetCaptureCamera()
    {
        if (OperatingSystem.IsWindows())
        {
           return DirectShow.GetFiltes(DirectShow.DsGuid.CLSID_VideoInputDeviceCategory);
        }
        else if (OperatingSystem.IsMacOS())
        {
            return [];
        }
        else if (OperatingSystem.IsLinux())
        {
            return [];
        }
        return [];
    }

    public static IEnumerable<(string, int)> GetCaptureTypes()
    {
        var captureTypes = new Dictionary<string, int>();
        var values = Enum.GetValues(typeof(VideoCaptureAPIs));

        foreach (var value in values)
        {
            yield return(value.ToString(), (int)value);
        }
    }
}
