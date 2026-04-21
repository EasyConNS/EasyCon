using System.IO;
using EasyCon2.UI.Common.Properties;

namespace EasyCon2;

internal static class ResourceHelper
{
    public static Bitmap Clrlog => new(new MemoryStream(Resources.clrlog));
    public static Icon CaptureVideoIcon => new(new MemoryStream(Resources.CaptureVideo));
}
