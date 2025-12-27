using EasyCapture;

namespace EasyCon.Core;

public partial class ECCore
{
    public static IEnumerable<string> GetCaptureSources() => ECCapture.GetCaptureCamera();

    public static IEnumerable<(string, int)> GetCaptureTypes() => ECCapture.GetCaptureTypes();

    public static List<string> GetDeviceNames() => EasyDevice.ECDevice.GetPortNames();

    public static IEnumerable<SearchMethod> GetSearchMethods() => ECSearch.GetEnableSearchMethods();
}
