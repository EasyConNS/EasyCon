using System.Drawing;
using System.Drawing.Imaging;

namespace EasyCapture;

public sealed class ECSearch
{
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

    public static List<Point> FindOCR(string text, Bitmap srcBmp, out double matchDegree)
    {
        using MemoryStream memoryStream = new MemoryStream();
        srcBmp.Save(memoryStream, ImageFormat.Bmp);
        matchDegree = OCRSearch.TesserDetect(memoryStream, out var resultTxt);
        return [new Point(0,0)];
    }

    public static List<Point> FindPic(int left, int top, int width, int height, Bitmap S_bmp, Bitmap srcBmp, SearchMethod method, out double matchDegree)
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

        List<Point> List = [];
        switch (method)
        {
            case SearchMethod.SqDiff:
            case SearchMethod.SqDiffNormed:
            case SearchMethod.CCorr:
            case SearchMethod.CCorrNormed:
            case SearchMethod.CCoeff:
            case SearchMethod.CCoeffNormed:
                List = OpenCVSearch.OpenCvFindPic(left, top, width, height, S_bmp, srcBmp, method, out matchDegree);
                break;
            case SearchMethod.EdgeDetectXY:
                List = OpenCVSearch.EdgeDetectXY(left, top, width, height, S_bmp, srcBmp, method, out matchDegree);
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
}
