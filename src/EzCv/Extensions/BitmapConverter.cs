using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace EzCv.Extensions;

/// <summary>
/// Mat → System.Drawing.Bitmap 转换器（替代 OpenCvSharp.Extensions.BitmapConverter）。
/// 仅用于 EasyCon2 WinForms 项目（CaptureVideoForm）。
/// </summary>
public static class BitmapConverter
{
    public static Bitmap ToBitmap(Mat mat)
    {
        if (mat == null || mat.Empty())
            throw new ArgumentException("Mat is null or empty");

        int width = mat.Width;
        int height = mat.Height;
        int channels = mat.Channels();

        var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);

        var rect = new Rectangle(0, 0, width, height);
        var bmpData = bitmap.LockBits(rect, ImageLockMode.WriteOnly, bitmap.PixelFormat);

        unsafe
        {
            byte* src = (byte*)mat.Data;
            int srcStep = (int)mat.Step();
            byte* dst = (byte*)bmpData.Scan0;
            int dstStep = bmpData.Stride;

            if (channels == 1)
            {
                // 灰度 → RGB
                for (int y = 0; y < height; y++)
                {
                    byte* srcRow = src + y * srcStep;
                    byte* dstRow = dst + y * dstStep;
                    for (int x = 0; x < width; x++)
                    {
                        byte g = srcRow[x];
                        dstRow[x * 3] = g;
                        dstRow[x * 3 + 1] = g;
                        dstRow[x * 3 + 2] = g;
                    }
                }
            }
            else
            {
                // BGR → RGB
                for (int y = 0; y < height; y++)
                {
                    byte* srcRow = src + y * srcStep;
                    byte* dstRow = dst + y * dstStep;
                    for (int x = 0; x < width; x++)
                    {
                        dstRow[x * 3] = srcRow[x * 3 + 2];     // R
                        dstRow[x * 3 + 1] = srcRow[x * 3 + 1]; // G
                        dstRow[x * 3 + 2] = srcRow[x * 3];     // B
                    }
                }
            }
        }

        bitmap.UnlockBits(bmpData);
        return bitmap;
    }
}