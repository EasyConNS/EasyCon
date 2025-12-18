using System.Collections.Immutable;
using EasyCapture;

namespace EasyCon.Core;

public partial class ECCore
{
    public static ImmutableArray<string> GetCaptureSources() => ECCapture.GetCaptureCamera();

    public static List<string> GetDeviceNames() => EasyDevice.ECDevice.GetPortNames();

    public static IEnumerable<SearchMethod> GetSearchMethods() => ECSearch.GetEnableSearchMethods();
}
