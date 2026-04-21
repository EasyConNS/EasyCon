using EasyCon.Capture;
using OpenCvSharp;
using System.Drawing;
using System.Runtime.Versioning;

public static class ImgLabelExt
{
    /// <summary>
    /// 从图像中获取指定范围的位图
    /// </summary>
    public static Mat GetRange(this Mat self, System.Drawing.Point pos, ImgLabel img)
    {
        return new Mat(self, new Rect(pos.X + img.RangeX, pos.Y + img.RangeY, img.TargetWidth, img.TargetHeight));
    }
}