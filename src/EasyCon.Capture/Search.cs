using EasyCon.Capture.Ocr;
using EzCv;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using Point = System.Drawing.Point;

namespace EasyCon.Capture;

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
            SearchMethod.MaskedSqDiffNormed,
            SearchMethod.EdgeDetectXY,
            SearchMethod.EdgeDetectLaplacian,
            SearchMethod.TesserDetect,
        ];
    }

    public static string FindOCR(string text, Mat srcBmp, out double matchDegree, string dataPath)
    {
        var imageBytes = srcBmp.ToBytes(".png");
        var factory = new TesseractEngineFactory();
        using var recognizer = factory.CreateRecognizer("chi_sim", dataPath, "DEFAULT", "SINGLE_LINE");
        var result = recognizer.Recognize(imageBytes);
        var resultTxt = result.Text.Trim();
        Debug.WriteLine($"识别到的文本：{resultTxt}, 匹配度:{result.Confidence}");
        Debug.WriteLine($"对比原始文本:{text}，对比对象：{resultTxt}");
        // 计算编辑距离
        matchDegree = MatchFacts.StringMatchSimple(resultTxt, text);
        // 置信度*编辑距离为最终相似度
        matchDegree *= result.Confidence;
        return resultTxt;
    }

    public static Point FindPic(Mat big, Mat small, SearchMethod method, out double matchDegree)
    {
        EzCv.Point result = new(-1, -1);
        switch (method)
        {
            case SearchMethod.SqDiff:
            case SearchMethod.SqDiffNormed:
            case SearchMethod.CCorr:
            case SearchMethod.CCorrNormed:
            case SearchMethod.CCoeff:
            case SearchMethod.CCoeffNormed:
                result = MatchFacts.MatchTemplate(big, small, method, out matchDegree);
                break;
            case SearchMethod.EdgeDetectXY:
            case SearchMethod.EdgeDetectLaplacian:
                {
                    using var bigResult = OpenCVSearch.EdgeDetect(big, method);
                    using var smallResult = OpenCVSearch.EdgeDetect(small, method);
                    result = MatchFacts.MatchTemplate(bigResult, smallResult, method, out matchDegree);
                    break;
                }
            default:
                // 不支持的匹配算法
                matchDegree = 0;
                break;
        }

        return new Point(result.X, result.Y);
    }
}

public static class ILExtLeg
{
    public static List<Point> Search(this ImgLabel self, Mat ss, out double md, string tessdataPath)
    {
        if (self.TargetWidth > self.RangeWidth || self.TargetHeight > self.RangeHeight)
            throw new Exception($"搜图标签[{self.name}]搜索图片大于搜索范围\n" +
                $"  搜图范围(ROI): X={self.RangeX}, Y={self.RangeY}, W={self.RangeWidth}, H={self.RangeHeight}\n" +
                $"  目标区域(Target): X={self.TargetX}, Y={self.TargetY}, W={self.TargetWidth}, H={self.TargetHeight}");

        try
        {
            Console.Error.WriteLine($"[Search] ss size: {ss.Width}x{ss.Height}, channels={ss.Channels()}, type={ss.Type()}, roi=({self._round.X},{self._round.Y},{self._round.Width},{self._round.Height})");
            using var range = new Mat(ss, self._round);

            List<Point> result = new();
            if (self.searchMethod == SearchMethod.TesserDetect)
            {
                var rlttxt = ECSearch.FindOCR(self.ImgBase64, range, out md, tessdataPath);
                result = [new Point(0, 0)];
            }
            else
            {
                if (self.searchMethod == SearchMethod.MaskedSqDiffNormed)
                {
                    var targetRGBA = self.GetCachedTargetMatRGBA();
                    if (targetRGBA.Channels() != 4)
                        throw new Exception("Masked matching requires RGBA image");
                    Cv2.Split(targetRGBA, out var channels);
                    using var bgr = new Mat();
                    Cv2.Merge([channels[0], channels[1], channels[2]], bgr);
                    using var mask = channels[3];
                    var pt = MatchFacts.MatchTemplateMasked(range, bgr, mask, out md);
                    result = [new Point(pt.X, pt.Y)];
                }
                else
                {
                    var target = self.GetCachedTargetMat();
                    result = [ECSearch.FindPic(range, target, self.searchMethod, out md)];
                }
            }
            md *= 100;

            return result;
        }
        catch (OpenCVException ex)
        {
            throw new Exception($"搜图标签[{self.name}]执行异常：{ex.Message}\n" +
                $"  搜图范围(ROI): X={self.RangeX}, Y={self.RangeY}, W={self.RangeWidth}, H={self.RangeHeight}\n" +
                $"  目标区域(Target): X={self.TargetX}, Y={self.TargetY}, W={self.TargetWidth}, H={self.TargetHeight}");
        }
    }
}