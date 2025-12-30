using EasyCon.Capture;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.Drawing;

public static class ImgLabelExt
{
    public static Bitmap GetRange(this Mat self, System.Drawing.Point pos, ImgLabel img)
    {
        using var range = new Mat(self, new Rect(pos.X + img.RangeX, pos.Y + img.RangeY, img.TargetWidth, img.TargetHeight));
        return BitmapConverter.ToBitmap(range);
    }
}
