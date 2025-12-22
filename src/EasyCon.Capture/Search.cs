using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;

namespace EasyCapture;

public sealed class ECSearch
{
    public static ImgLabel LoadIL(string dir) => ImgLabel.Load(dir);
    public static IEnumerable<SearchMethod> GetEnableSearchMethods()
    {
        return
        [
            SearchMethod.SqDiffNormed,
            SearchMethod.CCorrNormed,
            SearchMethod.CCoeffNormed,
            SearchMethod.EdgeDetectXY,
            SearchMethod.EdgeDetectLaplacian,
            SearchMethod.TesserDetect,
        ];
    }

    public static List<System.Drawing.Point> FindOCR(string text, Bitmap srcBmp, out double matchDegree)
    {
        using MemoryStream memoryStream = new MemoryStream();
        srcBmp.Save(memoryStream, ImageFormat.Bmp);
        var confidence = OCRSearch.TesserDetect(memoryStream, out var resultTxt);
        Debug.WriteLine($"识别到的文本：{resultTxt}, 匹配度:{confidence}");
        // 计算编辑距离
        matchDegree = OCRSearch.StringMatchSimple(resultTxt, text);
        return [new System.Drawing.Point(0,0)];
    }

    public static List<System.Drawing.Point> FindPic(int left, int top, int width, int height, Bitmap targetBmp, Bitmap srcBmp, SearchMethod method, out double matchDegree)
    {
        if (targetBmp.PixelFormat != PixelFormat.Format24bppRgb) { throw new Exception("颜色格式只支持24位bmp"); }
        if (srcBmp.PixelFormat != PixelFormat.Format24bppRgb) { throw new Exception("颜色格式只支持24位bmp"); }
        //int S_Width = S_bmp.Width;
        //int S_Height = S_bmp.Height;
        //int P_Width = srcBmp.Width;
        //int P_Height = srcBmp.Height;
        //BitmapData S_Data = null;
        //BitmapData P_Data = null;
        //int similar = 10;

        using Mat src = BitmapConverter.ToMat(srcBmp);
        using Mat big = BitmapConverter.ToMat(targetBmp);

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
                List = OpenCVSearch.OpenCvFindPic(left, top, width, height, big, src, method, out matchDegree);
                break;
            case SearchMethod.EdgeDetectXY:
            case SearchMethod.EdgeDetectLaplacian:
                List = OpenCVSearch.EdgeDetect(left, top, width, height, big, src, method, out matchDegree);
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

    private static unsafe bool ScanColor(byte b1, byte g1, byte r1, byte b2, byte g2, byte r2, int similar)
    {
        if ((Math.Abs(b1 - b2)) > similar) { return false; } //B
        if ((Math.Abs(g1 - g2)) > similar) { return false; } //G
        if ((Math.Abs(r1 - r2)) > similar) { return false; } //R
        return true;
    }
    #endregion
}
