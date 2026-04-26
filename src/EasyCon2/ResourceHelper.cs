using EasyCon2.UI.Common.Properties;
using System.IO;

namespace EasyCon2;

internal static class ResourceHelper
{
    public static Bitmap Clrlog => new(new MemoryStream(Resources.clrlog));
    public static Icon CaptureVideoIcon => new(new MemoryStream(Resources.CaptureVideo));
}