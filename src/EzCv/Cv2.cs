using EzCv.Interop;

namespace EzCv;

/// <summary>
/// OpenCV 核心函数静态方法类，API 兼容 OpenCvSharp.Cv2。
/// </summary>
public static class Cv2
{
    // --- 颜色转换 ---

    public static void CvtColor(Mat src, Mat dst, ColorConversionCodes code)
    {
        EzCvDll.CvtColor(src.Handle, dst.Handle, (int)code);
        EzCvError.ThrowIfAny();
    }

    // --- 二值化 ---

    public static double Threshold(Mat src, Mat dst, double thresh, double maxval, ThresholdTypes type)
    {
        var r = EzCvDll.Threshold(src.Handle, dst.Handle, thresh, maxval, (int)type);
        EzCvError.ThrowIfAny();
        return r;
    }

    // --- Sobel ---

    public static void Sobel(Mat src, Mat dst, int ddepth, int dx, int dy, int ksize = 3)
    {
        EzCvDll.Sobel(src.Handle, dst.Handle, ddepth, dx, dy, ksize);
        EzCvError.ThrowIfAny();
    }

    // --- 加权加法 ---

    public static void AddWeighted(Mat src1, double alpha, Mat src2, double beta, double gamma, Mat dst)
    {
        EzCvDll.AddWeighted(src1.Handle, alpha, src2.Handle, beta, gamma, dst.Handle);
        EzCvError.ThrowIfAny();
    }

    // --- 高斯模糊 ---

    public static void GaussianBlur(Mat src, Mat dst, Size ksize, double sigma)
    {
        EzCvDll.GaussianBlur(src.Handle, dst.Handle, ksize.Width, ksize.Height, sigma);
        EzCvError.ThrowIfAny();
    }

    // --- Laplacian ---

    public static void Laplacian(Mat src, Mat dst, int ddepth, int ksize = 1)
    {
        EzCvDll.Laplacian(src.Handle, dst.Handle, ddepth, ksize);
        EzCvError.ThrowIfAny();
    }

    // --- 绝对值缩放 ---

    public static void ConvertScaleAbs(Mat src, Mat dst)
    {
        EzCvDll.ConvertScaleAbs(src.Handle, dst.Handle);
        EzCvError.ThrowIfAny();
    }

    // --- Canny ---

    public static void Canny(Mat src, Mat dst, double t1, double t2)
    {
        EzCvDll.Canny(src.Handle, dst.Handle, t1, t2);
        EzCvError.ThrowIfAny();
    }

    // --- 缩放 ---

    public static void Resize(Mat src, Mat dst, Size dsize, double fx = 0, double fy = 0, InterpolationFlags interpolation = InterpolationFlags.Linear)
    {
        EzCvDll.Resize(src.Handle, dst.Handle, dsize.Width, dsize.Height, fx, fy, (int)interpolation);
        EzCvError.ThrowIfAny();
    }

    // --- 边界填充 ---

    public static void CopyMakeBorder(Mat src, Mat dst, int top, int bottom, int left, int right, BorderTypes borderType, Scalar value)
    {
        EzCvDll.CopyMakeBorder(src.Handle, dst.Handle, top, bottom, left, right, (int)borderType, value.Val0, value.Val1, value.Val2);
        EzCvError.ThrowIfAny();
    }

    // --- 通道操作 ---

    public static void Split(Mat src, out Mat[] mv)
    {
        var handles = new IntPtr[4];
        int count = EzCvDll.Split(src.Handle, handles);
        EzCvError.ThrowIfAny();
        mv = new Mat[count];
        for (int i = 0; i < count; i++)
            mv[i] = new Mat(handles[i], ownsHandle: true);
    }

    public static void Merge(Mat[] mv, Mat dst)
    {
        var handles = new IntPtr[mv.Length];
        for (int i = 0; i < mv.Length; i++)
            handles[i] = mv[i].Handle;
        EzCvDll.Merge(handles, mv.Length, dst.Handle);
        EzCvError.ThrowIfAny();
    }

    // --- 模板匹配 ---

    public static void MatchTemplate(Mat image, Mat templ, Mat result, TemplateMatchModes method)
    {
        EzCvDll.MatchTemplate(image.Handle, templ.Handle, result.Handle, (int)method);
        EzCvError.ThrowIfAny();
    }

    public static void MatchTemplate(Mat image, Mat templ, Mat result, TemplateMatchModes method, Mat mask)
    {
        EzCvDll.MatchTemplateMasked(image.Handle, templ.Handle, result.Handle, (int)method, mask.Handle);
        EzCvError.ThrowIfAny();
    }

    // --- 最小最大值位置 ---

    public static void MinMaxLoc(Mat src, out double minVal, out double maxVal, out Point minLoc, out Point maxLoc)
    {
        EzCvDll.MinMaxLoc(src.Handle, out minVal, out maxVal, out int minX, out int minY, out int maxX, out int maxY);
        EzCvError.ThrowIfAny();
        minLoc = new Point(minX, minY);
        maxLoc = new Point(maxX, maxY);
    }

    // --- 轮廓 ---

    public static void FindContours(Mat image, out Point[][] contours, out HierarchyIndex[] hierarchy, RetrievalModes mode, ContourApproximationModes method)
    {
        int count = EzCvDll.FindContours(image.Handle, (int)mode, (int)method);
        EzCvError.ThrowIfAny();
        contours = new Point[count][];
        hierarchy = new HierarchyIndex[count];

        for (int i = 0; i < count; i++)
        {
            int ptCount = EzCvDll.ContourPointCount(i);
            if (ptCount == 0)
            {
                contours[i] = [];
                continue;
            }
            var xArr = new int[ptCount];
            var yArr = new int[ptCount];
            EzCvDll.ContourPoints(i, xArr, yArr, ptCount);
            contours[i] = new Point[ptCount];
            for (int j = 0; j < ptCount; j++)
                contours[i][j] = new Point(xArr[j], yArr[j]);
        }

        // hierarchy not returned via C API currently — fill with defaults
        EzCvDll.ClearContours();
    }

    public static Rect BoundingRect(IEnumerable<Point> contour)
    {
        var pts = contour.ToArray();
        if (pts.Length == 0) return new Rect(0, 0, 0, 0);
        int minX = pts[0].X, minY = pts[0].Y, maxX = pts[0].X, maxY = pts[0].Y;
        foreach (var p in pts)
        {
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }
        return new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    // --- 图像编解码 ---

    public static Mat ImDecode(byte[] data, ImreadModes flags)
    {
        unsafe
        {
            fixed (byte* p = data)
            {
                var h = EzCvDll.ImDecodeMem(p, data.Length, (int)flags);
                // 失败（坏数据 / 不支持格式）会通过 last_error 抛 OpenCVException
                EzCvError.ThrowIfAny();
                return new Mat(h, ownsHandle: true);
            }
        }
    }

    public static byte[] ImEncode(string ext, Mat img)
    {
        if (img == null || img.Empty())
            throw new OpenCVException("ImEncode: source Mat is null or empty");

        var extBytes = System.Text.Encoding.UTF8.GetBytes(ext + "\0");
        unsafe
        {
            fixed (byte* pExt = extBytes)
            {
                var pData = EzCvDll.ImEncodeMem(pExt, img.Handle, out int len);
                // 失败（不支持扩展名 / 无效图像）通过 last_error 抛 OpenCVException
                EzCvError.ThrowIfAny();
                var result = new byte[len];
                System.Runtime.InteropServices.Marshal.Copy(pData, result, 0, len);
                EzCvDll.FreeBuf(pData);
                return result;
            }
        }
    }
}

/// <summary>
/// 轮廓层级索引（对应 OpenCV 的 Vec4i）。
/// </summary>
public struct HierarchyIndex
{
    public int Next;
    public int Previous;
    public int Child;
    public int Parent;
}