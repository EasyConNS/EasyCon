using OpenCvSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace EasyCon.Capture;

internal static class MatExtensions
{
    // Mat 转 byte[]（JPEG格式）
    public static byte[] ToJpegBytes(this Mat mat, int quality = 95)
    {
        if (mat == null || mat.Empty())
            return Array.Empty<byte>();

        try
        {
            using var image = MatToImageSharp(mat);
            using var ms = new MemoryStream();
            var encoder = new JpegEncoder
            {
                Quality = quality
            };
            image.Save(ms, encoder);
            return ms.ToArray();
        }
        catch
        {
            return Array.Empty<byte>();
        }
    }

    // Mat 转 byte[]（PNG格式）
    public static byte[] ToPngBytes(this Mat mat, int compressionLevel = 6)
    {
        if (mat == null || mat.Empty())
            return Array.Empty<byte>();

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
            return Array.Empty<byte>();
        }
    }

    // byte[] 转 Mat（自动检测格式）
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

    // 将 OpenCvSharp Mat 转换为 ImageSharp Image
    // 注意：OpenCvSharp Mat 是 BGR 格式，需要转换为 RGB
    private static Image<Rgba32> MatToImageSharp(Mat mat)
    {
        var image = new Image<Rgba32>(mat.Width, mat.Height);
        
        if (mat.Empty())
            return image;

        // 如果是灰度图
        if (mat.Channels() == 1)
        {
            using var grayMat = mat;
            for (int y = 0; y < mat.Height; y++)
            {
                for (int x = 0; x < mat.Width; x++)
                {
                    byte gray = grayMat.Get<byte>(y, x);
                    image[x, y] = new Rgba32(gray, gray, gray, 255);
                }
            }
        }
        // 如果是 BGR 图（3通道）
        else if (mat.Channels() == 3)
        {
            using var rgbMat = new Mat();
            Cv2.CvtColor(mat, rgbMat, ColorConversionCodes.BGR2RGBA);
            
            for (int y = 0; y < mat.Height; y++)
            {
                for (int x = 0; x < mat.Width; x++)
                {
                    var pixel = rgbMat.Get<Vec4b>(y, x);
                    image[x, y] = new Rgba32(pixel[2], pixel[1], pixel[0], pixel[3]);
                }
            }
        }
        // 如果是 BGRA 图（4通道）
        else if (mat.Channels() == 4)
        {
            using var rgbaMat = new Mat();
            Cv2.CvtColor(mat, rgbaMat, ColorConversionCodes.BGRA2RGBA);
            
            for (int y = 0; y < mat.Height; y++)
            {
                for (int x = 0; x < mat.Width; x++)
                {
                    var pixel = rgbaMat.Get<Vec4b>(y, x);
                    image[x, y] = new Rgba32(pixel[2], pixel[1], pixel[0], pixel[3]);
                }
            }
        }
        else
        {
            // 其他格式尝试直接转换
            using var converted = new Mat();
            Cv2.CvtColor(mat, converted, ColorConversionCodes.BGR2RGBA);
            
            for (int y = 0; y < mat.Height; y++)
            {
                for (int x = 0; x < mat.Width; x++)
                {
                    var pixel = converted.Get<Vec4b>(y, x);
                    image[x, y] = new Rgba32(pixel[2], pixel[1], pixel[0], pixel[3]);
                }
            }
        }

        return image;
    }

    // 将 ImageSharp Image 转换为 OpenCvSharp Mat
    // ImageSharp 是 RGB 格式，需要转换为 BGR
    private static Mat ImageSharpToMat(Image<Rgba32> image)
    {
        var mat = new Mat(image.Height, image.Width, MatType.CV_8UC3);

        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                var pixel = image[x, y];
                // RGB -> BGR
                mat.Set(y, x, new Vec3b(pixel.G, pixel.B, pixel.R));
            }
        }

        return mat;
    }
}