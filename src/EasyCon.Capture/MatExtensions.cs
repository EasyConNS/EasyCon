using EzCv;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace EasyCon.Capture;

public static class MatExtensions
{
    public static Mat Resize(this Mat src, double f = 0.5)
    {
        var newm = new Mat();
        Cv2.Resize(src, newm, new EzCv.Size(0, 0), f, f, InterpolationFlags.Area);
        return newm;
    }

    // Mat 转 byte[]（PNG格式）
    public static byte[] ToPngBytes(this Mat mat, int compressionLevel = 6)
    {
        if (mat == null || mat.Empty())
            return [];

        try
        {
            using var image = MatToImageSharp(mat);
            using var ms = new MemoryStream();
            var encoder = new PngEncoder
            {
                CompressionLevel = (PngCompressionLevel)Math.Clamp(compressionLevel, 0, 9)
            };
            image.Save(ms, encoder);
            return ms.ToArray();
        }
        catch
        {
            return [];
        }
    }

    // byte[] 转 Mat（通过 ImageSharp 解码，自动检测格式）
    public static Mat ToMat(this byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
            return new Mat();

        try
        {
            using var image = Image.Load<Rgba32>(bytes);
            return ImageSharpToMat(image);
        }
        catch
        {
            return new Mat();
        }
    }

    // Mat -> ImageSharp Rgba32（BGR/灰度 -> RGBA）
    public static Image<Rgba32> MatToImageSharp(Mat mat)
    {
        int w = mat.Width;
        int h = mat.Height;
        var image = new Image<Rgba32>(w, h);

        if (mat.Empty())
            return image;

        int channels = mat.Channels();

        if (channels == 1)
        {
            // 灰度 -> RGBA
            unsafe
            {
                byte* pSrc = (byte*)mat.Data;
                int step = (int)mat.Step();
                for (int y = 0; y < h; y++)
                {
                    byte* row = pSrc + y * step;
                    for (int x = 0; x < w; x++)
                    {
                        byte g = row[x];
                        image[x, y] = new Rgba32(g, g, g, 255);
                    }
                }
            }
        }
        else
        {
            // BGR/BGRA -> RGBA（统一转成4通道再提取）
            using var rgba = new Mat();
            if (channels == 4)
                Cv2.CvtColor(mat, rgba, ColorConversionCodes.BGRA2RGBA);
            else
                Cv2.CvtColor(mat, rgba, ColorConversionCodes.BGR2RGBA);

            unsafe
            {
                byte* pSrc = (byte*)rgba.Data;
                int step = (int)rgba.Step();
                for (int y = 0; y < h; y++)
                {
                    byte* row = pSrc + y * step;
                    for (int x = 0; x < w; x++)
                    {
                        int off = x * 4;
                        image[x, y] = new Rgba32(row[off], row[off + 1], row[off + 2], row[off + 3]);
                    }
                }
            }
        }

        return image;
    }

    // 裁剪 base64 图片的指定区域并返回 base64
    public static string? CropBase64(string base64, int x, int y, int w, int h)
    {
        var bytes = Convert.FromBase64String(base64);
        using var mat = bytes.ToMat();
        if (mat.Empty()) return null;
        x = Math.Clamp(x, 0, mat.Width);
        y = Math.Clamp(y, 0, mat.Height);
        w = Math.Clamp(w, 0, mat.Width - x);
        h = Math.Clamp(h, 0, mat.Height - y);
        if (w == 0 || h == 0) return null;
        using var roi = new Mat(mat, new Rect(x, y, w, h));
        return Convert.ToBase64String(roi.ToPngBytes());
    }

    // ImageSharp Rgba32 -> Mat（RGBA -> BGR）
    public static Mat ImageSharpToMat(Image<Rgba32> image)
    {
        int w = image.Width;
        int h = image.Height;
        var mat = new Mat(h, w, MatType.CV_8UC3);

        unsafe
        {
            byte* pDst = (byte*)mat.Data;
            int step = (int)mat.Step();

            for (int y = 0; y < h; y++)
            {
                byte* row = pDst + y * step;
                for (int x = 0; x < w; x++)
                {
                    var pixel = image[x, y];
                    int off = x * 3;
                    row[off] = pixel.B;     // B
                    row[off + 1] = pixel.G; // G
                    row[off + 2] = pixel.R; // R
                }
            }
        }

        return mat;
    }
}