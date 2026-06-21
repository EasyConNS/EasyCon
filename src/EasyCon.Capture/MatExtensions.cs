using EzCv;

namespace EasyCon.Capture;

public static class MatExtensions
{
    public static Mat Resize(this Mat src, double f = 0.5)
    {
        var newm = new Mat();
        Cv2.Resize(src, newm, new EzCv.Size(0, 0), f, f, InterpolationFlags.Area);
        return newm;
    }

    // byte[] 转 Mat（通过 OpenCV imdecode 解码，自动检测格式）
    public static Mat ToMat(this byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
            return new Mat();

        return Cv2.ImDecode(bytes, ImreadModes.Color);
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
        return Convert.ToBase64String(roi.ToBytes(".png"));
    }
}