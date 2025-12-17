using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;

namespace EasyCapture;

public class OpenCVSearch : ECSearch
{
    public static List<System.Drawing.Point> FindPic(int left, int top, int width, int height, Bitmap S_bmp, Bitmap srcBmp, SearchMethod method, out double matchDegree)
    {
        if (S_bmp.PixelFormat != PixelFormat.Format24bppRgb) { throw new Exception("颜色格式只支持24位bmp"); }
        if (srcBmp.PixelFormat != PixelFormat.Format24bppRgb) { throw new Exception("颜色格式只支持24位bmp"); }
        int S_Width = S_bmp.Width;
        int S_Height = S_bmp.Height;
        int P_Width = srcBmp.Width;
        int P_Height = srcBmp.Height;
        //BitmapData S_Data = null;
        //BitmapData P_Data = null;
        //int similar = 10;

        //if ((int)method > 5)
        //{
        //    S_Data = S_bmp.LockBits(new Rectangle(0, 0, S_Width, S_Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        //    P_Data = srcBmp.LockBits(new Rectangle(0, 0, P_Width, P_Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        //}

        List<System.Drawing.Point> List = [];
        switch (method)
        {
            case SearchMethod.SqDiff:
            case SearchMethod.SqDiffNormed:
            case SearchMethod.CCorr:
            case SearchMethod.CCorrNormed:
            case SearchMethod.CCoeff:
            case SearchMethod.CCoeffNormed:
                List = OpenCvFindPic(left, top, width, height, S_bmp, srcBmp, method, out matchDegree);
                break;
            case SearchMethod.EdgeDetectXY:
                List = EdgeDetectXY(left, top, width, height, S_bmp, srcBmp, method, out matchDegree);
                break;
            // case SearchMethod.StrictMatch:
            //     List = StrictMatch(left, top, width, height, S_Data, P_Data);
            //     matchDegree = 1;
            //     break;
            // case SearchMethod.StrictMatchRND:
            //     List = StrictMatchRND(left, top, width, height, S_Data, P_Data);
            //     matchDegree = 1;
            //     break;
            // case SearchMethod.OpacityDiff:
            //     Color BackColor = srcBmp.GetPixel(0, 0); //背景色
            //     List = OpacityDiff(left, top, width, height, S_Data, P_Data, GetPixelData(P_Data, BackColor), similar);
            //     matchDegree = 1;
            //     break;
            // case SearchMethod.SimilarMatch:
            //     List = SimilarMatch(left, top, width, height, S_Data, P_Data, similar);
            //     matchDegree = 1;
            //     break;
            default:
            // 不支持的匹配算法
                matchDegree = 0;
                break;
        }

        //if ((int)method > 5)
        //{
        //    S_bmp.UnlockBits(S_Data);
        //    srcBmp.UnlockBits(P_Data);
        //}

        return List;
    }

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


    private static List<System.Drawing.Point> EdgeDetectXY(int left, int top, int width, int height, Bitmap targetBmp, Bitmap srcBmp, SearchMethod method, out double matchDegree)
    {
        using Mat src = BitmapConverter.ToMat(srcBmp);
        using Mat big = BitmapConverter.ToMat(targetBmp);

        using var result = method == SearchMethod.EdgeDetectLaplacian? LaplacianEdge(src) : XYAvg(src);
        //using (new Window("结果1", result))
        //{
        //    Cv2.WaitKey(0);
        //}
        using var result2 = method == SearchMethod.EdgeDetectLaplacian ? LaplacianEdge(big) : XYAvg(big);
        //using (new Window("结果2", result2))
        //{
        //    Cv2.WaitKey(0);
        //}

        return OpenCvFindPic(left, top, width, height, result2, result, method, out matchDegree);
    }

    #region 其他匹配算法
    private static unsafe List<System.Drawing.Point> StrictMatch(int left, int top, int width, int height, BitmapData S_Data, BitmapData P_Data)
    {
        List<System.Drawing.Point> List = new List<System.Drawing.Point>();
        int S_stride = S_Data.Stride;
        int P_stride = P_Data.Stride;
        IntPtr S_Iptr = S_Data.Scan0;
        IntPtr P_Iptr = P_Data.Scan0;
        byte* S_ptr;
        byte* P_ptr;
        bool IsOk = false;
        int _BreakW = width - P_Data.Width + 1;
        int _BreakH = height - P_Data.Height + 1;
        for (int h = top; h < _BreakH; h++)
        {
            for (int w = left; w < _BreakW; w++)
            {
                P_ptr = (byte*)(P_Iptr);
                // there could be a random check for quick jump the loop
                for (int y = 0; y < P_Data.Height; y++)
                {
                    for (int x = 0; x < P_Data.Width; x++)
                    {
                        S_ptr = (byte*)((int)S_Iptr + S_stride * (h + y) + (w + x) * 3);
                        P_ptr = (byte*)((int)P_Iptr + P_stride * y + x * 3);
                        if (S_ptr[0] == P_ptr[0] && S_ptr[1] == P_ptr[1] && S_ptr[2] == P_ptr[2])
                        {
                            IsOk = true;
                        }
                        else
                        {
                            IsOk = false;
                            break;
                        }
                    }
                    if (!IsOk) { break; }
                }
                if (IsOk) { List.Add(new System.Drawing.Point(w, h)); }
                IsOk = false;
            }
        }
        return List;
    }

    private static unsafe List<System.Drawing.Point> StrictMatchRND(int left, int top, int width, int height, BitmapData S_Data, BitmapData P_Data)
    {
        List<System.Drawing.Point> List = new List<System.Drawing.Point>();
        int S_stride = S_Data.Stride;
        int P_stride = P_Data.Stride;
        IntPtr S_Iptr = S_Data.Scan0;
        IntPtr P_Iptr = P_Data.Scan0;
        byte* S_ptr;
        byte* P_ptr;
        bool IsOk = false;
        int _BreakW = width - P_Data.Width + 1;
        int _BreakH = height - P_Data.Height + 1;

        Random r = new Random();

        // fitst we generate a random num list
        int pix_num = P_Data.Height * P_Data.Width;
        List<int> pix_list = new List<int>();
        for (int i = 0; i < pix_num; i++)
        {
            pix_list.Add(i);
        }

        List<int> random_pix_list = new List<int>();
        for (int i = 0; i < pix_num; i++)
        {
            int index = r.Next(pix_num - i);
            //Debug.WriteLine(index);
            random_pix_list.Add(pix_list[index]);
            pix_list.RemoveAt(index);
        }

        //Debug.WriteLine("WH:"+P_Data.Width.ToString() + " " + P_Data.Height.ToString());

        for (int h = top; h < _BreakH; h++)
        {
            for (int w = left; w < _BreakW; w++)
            {
                P_ptr = (byte*)(P_Iptr);
                // there could be a random check for quick jump the loop
                for (int i = 0; i < pix_num; i++)
                {
                    int y = (int)(random_pix_list[i] / P_Data.Width);
                    int x = random_pix_list[i] % P_Data.Width;
                    //Debug.WriteLine(random_pix_list[i]);
                    //Debug.WriteLine(x.ToString() +" "+ y.ToString());
                    S_ptr = (byte*)((int)S_Iptr + S_stride * (h + y) + (w + x) * 3);
                    P_ptr = (byte*)((int)P_Iptr + P_stride * y + x * 3);

                    //Debug.WriteLine(S_ptr[0].ToString() + " " + S_ptr[1].ToString() + " " + S_ptr[2].ToString());
                    //Debug.WriteLine(P_ptr[0].ToString() + " " + P_ptr[1].ToString() + " " + P_ptr[2].ToString());

                    if (S_ptr[0] == P_ptr[0] && S_ptr[1] == P_ptr[1] && S_ptr[2] == P_ptr[2])
                    {
                        IsOk = true;
                    }
                    else
                    {
                        IsOk = false;
                        break;
                    }
                }

                if (IsOk)
                {
                    //Debug.WriteLine("find");
                    List.Add(new System.Drawing.Point(w, h));
                }
                IsOk = false;
            }
        }
        return List;
    }

    private static unsafe List<System.Drawing.Point> SimilarMatch(int left, int top, int width, int height, BitmapData S_Data, BitmapData P_Data, int similar)
    {
        List<System.Drawing.Point> List = new List<System.Drawing.Point>();
        int S_stride = S_Data.Stride;
        int P_stride = P_Data.Stride;
        IntPtr S_Iptr = S_Data.Scan0;
        IntPtr P_Iptr = P_Data.Scan0;
        byte* S_ptr;
        byte* P_ptr;
        bool IsOk = false;
        int _BreakW = width - P_Data.Width + 1;
        int _BreakH = height - P_Data.Height + 1;
        for (int h = top; h < _BreakH; h++)
        {
            for (int w = left; w < _BreakW; w++)
            {
                P_ptr = (byte*)(P_Iptr);
                for (int y = 0; y < P_Data.Height; y++)
                {
                    for (int x = 0; x < P_Data.Width; x++)
                    {
                        S_ptr = (byte*)((int)S_Iptr + S_stride * (h + y) + (w + x) * 3);
                        P_ptr = (byte*)((int)P_Iptr + P_stride * y + x * 3);
                        if (ScanColor(S_ptr[0], S_ptr[1], S_ptr[2], P_ptr[0], P_ptr[1], P_ptr[2], similar))  //比较颜色
                        {
                            IsOk = true;
                        }
                        else
                        {
                            IsOk = false; break;
                        }
                    }
                    if (IsOk == false) { break; }
                }
                if (IsOk) { List.Add(new System.Drawing.Point(w, h)); }
                IsOk = false;
            }
        }
        return List;
    }

    private static unsafe List<System.Drawing.Point> OpacityDiff(int left, int top, int width, int height, BitmapData S_Data, BitmapData P_Data, int[,] PixelData, int similar)
    {
        List<System.Drawing.Point> List = new List<System.Drawing.Point>();
        int Len = PixelData.GetLength(0);
        int S_stride = S_Data.Stride;
        int P_stride = P_Data.Stride;
        IntPtr S_Iptr = S_Data.Scan0;
        IntPtr P_Iptr = P_Data.Scan0;
        byte* S_ptr;
        byte* P_ptr;
        bool IsOk = false;
        int _BreakW = width - P_Data.Width + 1;
        int _BreakH = height - P_Data.Height + 1;
        for (int h = top; h < _BreakH; h++)
        {
            for (int w = left; w < _BreakW; w++)
            {
                for (int i = 0; i < Len; i++)
                {
                    S_ptr = (byte*)((int)S_Iptr + S_stride * (h + PixelData[i, 1]) + (w + PixelData[i, 0]) * 3);
                    P_ptr = (byte*)((int)P_Iptr + P_stride * PixelData[i, 1] + PixelData[i, 0] * 3);
                    if (ScanColor(S_ptr[0], S_ptr[1], S_ptr[2], P_ptr[0], P_ptr[1], P_ptr[2], similar))  //比较颜色
                    {
                        IsOk = true;
                    }
                    else
                    {
                        IsOk = false; break;
                    }
                }
                if (IsOk) { List.Add(new System.Drawing.Point(w, h)); }
                IsOk = false;
            }
        }
        return List;
    }

    private static unsafe List<System.Drawing.Point> FindColor(int left, int top, int width, int height, Bitmap S_bmp, Color clr, int similar)
    {
        if (S_bmp.PixelFormat != PixelFormat.Format24bppRgb) { throw new Exception("颜色格式只支持24位bmp"); }
        BitmapData S_Data = S_bmp.LockBits(new Rectangle(0, 0, S_bmp.Width, S_bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        IntPtr _Iptr = S_Data.Scan0;
        byte* _ptr;
        List<System.Drawing.Point> List = new List<System.Drawing.Point>();
        for (int y = top; y < height; y++)
        {
            for (int x = left; x < width; x++)
            {
                _ptr = (byte*)((int)_Iptr + S_Data.Stride * (y) + (x) * 3);
                if (ScanColor(_ptr[0], _ptr[1], _ptr[2], clr.B, clr.G, clr.R, similar))
                {
                    List.Add(new System.Drawing.Point(x, y));
                }
            }
        }
        S_bmp.UnlockBits(S_Data);
        return List;
    }

    private static unsafe int[,] GetPixelData(BitmapData P_Data, Color BackColor)
    {
        byte B = BackColor.B, G = BackColor.G, R = BackColor.R;
        int Width = P_Data.Width, Height = P_Data.Height;
        int P_stride = P_Data.Stride;
        IntPtr P_Iptr = P_Data.Scan0;
        byte* P_ptr;
        int[,] PixelData = new int[Width * Height, 2];
        int i = 0;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                P_ptr = (byte*)((int)P_Iptr + P_stride * y + x * 3);
                if (B == P_ptr[0] & G == P_ptr[1] & R == P_ptr[2])
                {

                }
                else
                {
                    PixelData[i, 0] = x;
                    PixelData[i, 1] = y;
                    i++;
                }
            }
        }
        int[,] PixelData2 = new int[i, 2];
        Array.Copy(PixelData, PixelData2, i * 2);
        return PixelData2;
    }
    #endregion
    private static List<System.Drawing.Point> OpenCvFindPic(int left, int top, int width, int height, Bitmap targetBmp, Bitmap srcBmp, SearchMethod method, out double matchDegree)
    {
        using Mat small = BitmapConverter.ToMat(srcBmp);
        using Mat big = BitmapConverter.ToMat(targetBmp);
        return OpenCvFindPic(left, top, width, height, big, small, method, out matchDegree);
    }

    private static List<System.Drawing.Point> OpenCvFindPic(int left, int top, int width, int height, Mat big, Mat small, SearchMethod method, out double matchDegree)
    {
        List<System.Drawing.Point> res = [];
        using var result = new Mat();
        var minLoc = new OpenCvSharp.Point(0, 0);
        var maxLoc = new OpenCvSharp.Point(0, 0);
        double max = 0, min = 0;
        matchDegree = 0;
        switch (method)
        {
            case SearchMethod.SqDiff:
                Cv2.MatchTemplate(big, small, result, TemplateMatchModes.SqDiff);
                Cv2.MinMaxLoc(result, out min, out max, out minLoc, out maxLoc);
                Debug.WriteLine($"SqDiff:{min} {max}");
                break;
            case SearchMethod.SqDiffNormed:
                Cv2.MatchTemplate(big, small, result, TemplateMatchModes.SqDiffNormed);
                Cv2.MinMaxLoc(result, out min, out max, out minLoc, out maxLoc);
                matchDegree = (1 - min) / 1.0;
                Debug.WriteLine($"SqDiffNormed:{min} {max}");
                break;
            case SearchMethod.CCorr:
                // not good for our usage
                Cv2.MatchTemplate(big, small, result, TemplateMatchModes.CCorr);
                Cv2.MinMaxLoc(result, out min, out max, out minLoc, out maxLoc);
                Debug.WriteLine($"CCorr:{min} {max}");
                break;
            case SearchMethod.CCorrNormed:
                Cv2.MatchTemplate(big, small, result, TemplateMatchModes.CCorrNormed);
                Cv2.MinMaxLoc(result, out min, out max, out minLoc, out maxLoc);
                Debug.WriteLine($"CCorrNormed:{min} {max}");
                matchDegree = max / 1.0;
                break;
            case SearchMethod.CCoeff:
                Cv2.MatchTemplate(big, small, result, TemplateMatchModes.CCoeff);
                Cv2.MinMaxLoc(result, out min, out max, out minLoc, out maxLoc);
                Debug.WriteLine($"CCoeff:{min} {max}");
                break;
            case SearchMethod.CCoeffNormed:
            default:
                Cv2.MatchTemplate(big, small, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out min, out max, out minLoc, out maxLoc);
                matchDegree = (max + 1) / 2.0;
                Debug.WriteLine($"CCoeffNormed:{min} {max}");
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

    private static unsafe bool ScanColor(byte b1, byte g1, byte r1, byte b2, byte g2, byte r2, int similar)
    {
        if ((Math.Abs(b1 - b2)) > similar) { return false; } //B
        if ((Math.Abs(g1 - g2)) > similar) { return false; } //G
        if ((Math.Abs(r1 - r2)) > similar) { return false; } //R
        return true;
    }
}
