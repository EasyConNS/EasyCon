using OpenCvSharp;

namespace EasyCon.Capture;

internal static class OpenCVSearch
{
    private static Mat CvtGray(Mat src)
    {
        var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
        return gray;
    }

    private static Mat Threshold(Mat src, double thr = 127, double maxval = 255)
    {
        var dest = new Mat();
        Cv2.Threshold(src, dest, thr, maxval, ThresholdTypes.Binary);
        return dest;
    }

    private static Mat XYAvg(Mat src)
    {
        // 1. 转换为灰度图像
        using Mat gray = CvtGray(src);

        // 2. 计算x方向梯度
        using Mat gradX = new();
        // ddepth: CV_16S避免溢出
        // dx=1, dy=0: 计算x方向梯度
        Cv2.Sobel(gray, gradX, MatType.CV_16S, 1, 0, -1);
        // 3. 计算y方向梯度
        using Mat gradY = new();
        // dx=0, dy=1: 计算y方向梯度
        Cv2.Sobel(gray, gradY, MatType.CV_16S, 0, 1, -1);
        // 方法1：算术平均值 (Gx + Gy) / 2
        using Mat result = new();
        Cv2.AddWeighted(gradX, 0.5, gradY, 0.5, 0, result);

        Mat result8U = new();
        result.ConvertTo(result8U, MatType.CV_8UC3);
        return result8U;
    }

    private static Mat LaplacianEdge(Mat src)
    {
        // 1. 转换为灰度图像
        using Mat gray = CvtGray(src);

        // 1. 高斯模糊降噪（Laplacian对噪声敏感，必须先降噪）
        using Mat blurred = new();
        Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 1.5);

        // 2. Laplacian边缘检测
        using Mat laplacian = new();
        // 参数说明：
        // ddepth: 输出图像深度，CV_16S避免溢出
        // ksize: 核大小，必须是正奇数
        Cv2.Laplacian(blurred, laplacian, MatType.CV_16S, 3);

        // 3. 转换为绝对值并缩放到8位
        using Mat absLaplacian = new();
        Cv2.ConvertScaleAbs(laplacian, absLaplacian);

        // 4. 二值化处理
        using Mat edges = Threshold(absLaplacian, 30);

        Mat result8U = new();
        edges.ConvertTo(result8U, MatType.CV_8UC3);
        return result8U;
    }

    private static Mat Canny(Mat src, double t1 = 50, double t2 = 200)
    {
        var dst = new Mat();
        Cv2.Canny(src, dst, t1, t2);
        return dst;
    }

    public static Mat EdgeDetect(Mat src, SearchMethod method)
    {
        return method switch
        {
            SearchMethod.EdgeDetectXY => XYAvg(src),
            SearchMethod.EdgeDetectLaplacian => LaplacianEdge(src),
            SearchMethod.EdgeDetectCanny => Canny(src),
            _ => throw new NotImplementedException(),
        };
    }
}
