using OpenCvSharp;
using System.Diagnostics;

namespace EasyCapture;

internal class OpenCVSearch : AbstractSearch
{
    private static Mat XYAvg(Mat src)
    {
        // 1. 转换为灰度图像
        using Mat gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

        // 2. 计算x方向梯度
        using Mat gradX = new Mat();
        // ddepth: CV_16S避免溢出
        // dx=1, dy=0: 计算x方向梯度
        Cv2.Sobel(gray, gradX, MatType.CV_16S, 1, 0, -1);
        // 3. 计算y方向梯度
        using Mat gradY = new Mat();
        // dx=0, dy=1: 计算y方向梯度
        Cv2.Sobel(gray, gradY, MatType.CV_16S, 0, 1, -1);
        // 方法1：算术平均值 (Gx + Gy) / 2
        using Mat result = new Mat();
        Cv2.AddWeighted(gradX, 0.5, gradY, 0.5, 0, result);

        Mat result8U = new();
        result.ConvertTo(result8U, MatType.CV_8UC3);
        return result8U;
    }

    private static Mat LaplacianEdge(Mat src)
    {
        // 1. 转换为灰度图像
        using Mat gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

        // 1. 高斯模糊降噪（Laplacian对噪声敏感，必须先降噪）
        using Mat blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new OpenCvSharp.Size(5, 5), 1.5);

        // 2. Laplacian边缘检测
        using Mat laplacian = new Mat();
        // 参数说明：
        // ddepth: 输出图像深度，CV_16S避免溢出
        // ksize: 核大小，必须是正奇数
        Cv2.Laplacian(blurred, laplacian, MatType.CV_16S, 3);

        // 3. 转换为绝对值并缩放到8位
        using Mat absLaplacian = new Mat();
        Cv2.ConvertScaleAbs(laplacian, absLaplacian);

        // 4. 二值化处理
        using Mat edges = new Mat();
        // 方法1：简单阈值
        Cv2.Threshold(absLaplacian, edges, 30, 255, ThresholdTypes.Binary);

        Mat result8U = new();
        edges.ConvertTo(result8U, MatType.CV_8UC3);
        return result8U;
    }

    public static List<System.Drawing.Point> EdgeDetect(int left, int top, int width, int height, Mat big, Mat small, SearchMethod method, out double matchDegree)
    {
        using var result = method == SearchMethod.EdgeDetectLaplacian? LaplacianEdge(small) : XYAvg(small);
        //using (new Window("结果1", result))
        //{
        //    Cv2.WaitKey(0);
        //}
        using var result2 = method == SearchMethod.EdgeDetectLaplacian ? LaplacianEdge(big) : XYAvg(big);
        //using (new Window("结果2", result2))
        //{
        //    Cv2.WaitKey(0);
        //}

        return OpenCvFindPic(left, top, width, height, result2, result, SearchMethod.CCoeffNormed, out matchDegree);
    }

    public static List<System.Drawing.Point> OpenCvFindPic(int left, int top, int width, int height, Mat big, Mat small, SearchMethod method, out double matchDegree)
    {
        List<System.Drawing.Point> res = [];
        using var result = new Mat();
        var minLoc = new OpenCvSharp.Point(0, 0);
        var maxLoc = new OpenCvSharp.Point(0, 0);
        double max = 0, min = 0;
        matchDegree = 0;
        var tmplMatchMode = method switch
        {
            SearchMethod.SqDiff => TemplateMatchModes.SqDiff,
            SearchMethod.SqDiffNormed => TemplateMatchModes.SqDiffNormed,
            SearchMethod.CCorr => TemplateMatchModes.CCorr,
            SearchMethod.CCorrNormed => TemplateMatchModes.CCorrNormed,
            SearchMethod.CCoeff => TemplateMatchModes.CCoeff,
            SearchMethod.CCoeffNormed => TemplateMatchModes.CCoeffNormed,
            _ => TemplateMatchModes.CCoeffNormed,
        };

        Cv2.MatchTemplate(big, small, result, tmplMatchMode);
        Cv2.MinMaxLoc(result, out min, out max, out minLoc, out maxLoc);

        Debug.WriteLine($"{method}[min:{min}, max:{max}]");
        switch (method)
        {
            case SearchMethod.SqDiff:
                matchDegree = min;
                break;
            case SearchMethod.SqDiffNormed:
                matchDegree = (1 - min) / 1.0;
                break;
            case SearchMethod.CCorrNormed:
                matchDegree = max / 1.0;
                break;
            case SearchMethod.CCoeffNormed:
                matchDegree = (max + 1) / 2.0;
                break;
        }
        // the sqD lower is good
        if (method == SearchMethod.SqDiff || method == SearchMethod.SqDiffNormed)
        {
            res.Add(new System.Drawing.Point(minLoc.X, minLoc.Y));
        }
        else
        {
            res.Add(new System.Drawing.Point(maxLoc.X, maxLoc.Y));
        }

        return res;
    }
}
