using OpenCvSharp;

namespace EasyCon.Capture;

internal static class MatExtensions
{
    // Mat 转 byte[]（JPEG格式）
    public static byte[] ToJpegBytes(this Mat mat, int quality = 95)
    {
        if (mat == null || mat.Empty())
            return Array.Empty<byte>();

        ImageEncodingParam[] param = quality == 95 ? [] : [new ImageEncodingParam(ImwriteFlags.JpegQuality, quality)];

        Cv2.ImEncode(".jpg", mat, out byte[] bytes, param);
        return bytes;
    }

    // Mat 转 byte[]（PNG格式）
    public static byte[] ToPngBytes(this Mat mat, int compressionLevel = 3)
    {
        if (mat == null || mat.Empty())
            return Array.Empty<byte>();

        ImageEncodingParam[] param = compressionLevel == 3 ? [] : [new(ImwriteFlags.PngCompression, compressionLevel)];

        Cv2.ImEncode(".png", mat, out byte[] bytes, param);
        return bytes;
    }

    // byte[] 转 Mat（自动检测格式）
    public static Mat ToMat(this byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
            return new Mat();

        return Cv2.ImDecode(bytes, ImreadModes.Color);
    }
}