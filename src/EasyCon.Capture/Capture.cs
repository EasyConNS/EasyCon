using OpenCvSharp;
using System.Collections.Immutable;
using System.Diagnostics;

namespace EasyCapture;

public sealed class ECCapture
{
    public static ImmutableArray<string> GetCaptureCamera()
    {
        if (OperatingSystem.IsWindows())
        {
           return DirectShow.GetFiltes(DirectShow.DsGuid.CLSID_VideoInputDeviceCategory);
        }
        else if (OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException();
        }
        else if (OperatingSystem.IsLinux())
        {
            return [];
        }
        else
        {
            return [];
        }
    }

    public static Dictionary<string, int> GetCaptureTypes() => getCaptureTypes();

    private static Dictionary<string, int> getCaptureTypes()
    {
        var captureTypes = new Dictionary<string, int>();
        var values = Enum.GetValues(typeof(VideoCaptureAPIs));
        foreach (var value in values)
        {
            Debug.WriteLine(value + "--" + (int)value);//获取名称和值
            if (captureTypes.ContainsKey(value.ToString()))
                continue;
            captureTypes.Add(value.ToString(), (int)value);
        }
        return captureTypes;
    }
}
