using EasyCon.Capture.Ocr;
using OpenCvSharp;
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

    /// <summary>
    /// OCR 引擎统一经 <see cref="OcrEngineCache"/> 管理（与 Core 侧 EngineCacheOcrService
    /// 同一条引擎生命周期路径）：按 lang+参数幂等初始化，失败时抛带原因的异常。
    /// 识别器非线程安全，串行化访问。
    /// </summary>
    private static readonly OcrEngineCache OcrEngines = new(new TesseractEngineFactory());

    public static string FindOCR(string text, Mat srcBmp, out double matchDegree, string dataPath)
    {
        var imageBytes = srcBmp.ToBytes(".png");
        lock (OcrEngines)
        {
            var recognizer = OcrEngines.GetOrInit("chi_sim", dataPath, "DEFAULT", "SINGLE_LINE");
            var result = recognizer.Recognize(imageBytes);
            var resultTxt = result.Text.Trim();

            // 计算编辑距离；置信度*编辑距离为最终相似度
            matchDegree = MatchFacts.StringMatchSimple(resultTxt, text) * result.Confidence;
            return resultTxt;
        }
    }

    public static Point FindPic(Mat big, Mat small, SearchMethod method, out double matchDegree)
    {
        OpenCvSharp.Point result = new(-1, -1);
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
                    using var targetRGBA = self.GetCachedTargetMatRGBA();
                    if (targetRGBA.Channels() != 4)
                        throw new Exception("Masked matching requires RGBA image");
                    Cv2.Split(targetRGBA, out var channels);
                    using var bgr = new Mat();
                    using var ch0 = channels[0];
                    using var ch1 = channels[1];
                    using var ch2 = channels[2];
                    using var mask = channels[3];
                    Cv2.Merge([ch0, ch1, ch2], bgr);
                    var pt = MatchFacts.MatchTemplateMasked(range, bgr, mask, out md);
                    result = [new Point(pt.X, pt.Y)];
                }
                else
                {
                    using var target = self.GetCachedTargetMat();
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