using EasyCon.Capture;
using OpenCvSharp;
#if WINDOWS
using OpenCvSharp.Extensions;
#endif
using System.Drawing;
using System.Runtime.Versioning;

public static class ImgLabelExt
{
    /// <summary>
    /// 从图像中获取指定范围的位图
    /// </summary>
    /// <remarks>仅在 Windows 平台上受支持</remarks>
    [SupportedOSPlatform("windows")]
    public static Bitmap GetRange(this Mat self, System.Drawing.Point pos, ImgLabel img)
    {
#if WINDOWS
        using var range = new Mat(self, new Rect(pos.X + img.RangeX, pos.Y + img.RangeY, img.TargetWidth, img.TargetHeight));
        return BitmapConverter.ToBitmap(range);
#else
        throw new PlatformNotSupportedException("GetRange 方法仅在 Windows 平台上受支持");
#endif
    }
}
