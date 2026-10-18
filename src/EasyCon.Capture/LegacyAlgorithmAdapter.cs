using OpenCvSharp;

namespace EasyCon.Capture;

public enum LegacyCompatibilityProfile { LegacyGui, LegacyCli }

public sealed record RawMatchResult(double Score, Rect Box, SearchMethod Method, string AlgorithmVersion = "easycon-opencv-legacy/1");

/// <summary>唯一的旧浮点分数及整数适配。保留旧运算顺序，避免整数边界上的浮点重排。</summary>
public static class LegacyAlgorithmAdapter
{
    public static double ToLegacyFloat(RawMatchResult result)
    {
        if (!double.IsFinite(result.Score)) throw new InvalidDataException("模板匹配产生 NaN/Infinity");
        return result.Method switch
        {
            SearchMethod.SqDiffNormed or SearchMethod.MaskedSqDiffNormed => (1 - result.Score) * 100,
            SearchMethod.CCorrNormed or SearchMethod.TesserDetect => result.Score * 100,
            SearchMethod.CCoeffNormed or SearchMethod.EdgeDetectXY or SearchMethod.EdgeDetectLaplacian => ((result.Score + 1) / 2.0) * 100,
            _ => throw new NotSupportedException($"不支持旧评分算法 {result.Method}"),
        };
    }

    public static int ToLegacyInteger(RawMatchResult result, LegacyCompatibilityProfile profile)
    {
        if (!Enum.IsDefined(profile)) throw new InvalidDataException($"未知兼容 profile {profile}");
        double value = ToLegacyFloat(result);
        return checked((int)(profile == LegacyCompatibilityProfile.LegacyCli ? Math.Ceiling(value) : Math.Truncate(value)));
    }

    public static double TextSimilarity(string actual, string expected) => MatchFacts.StringMatchSimple(actual.Trim(), expected);

    public static bool Compare(int score, string comparison, int threshold) => comparison switch
    {
        ">" => score > threshold,
        ">=" => score >= threshold,
        "<" => score < threshold,
        "<=" => score <= threshold,
        "==" => score == threshold,
        "!=" => score != threshold,
        _ => throw new InvalidDataException($"未知分数比较符 {comparison}"),
    };
}

/// <summary>供 Maa CustomRecognition 调用的旧 OpenCV 原始评分；不执行取整、不读取采集源。</summary>
public static class LegacyTemplateMatcher
{
    public static RawMatchResult Match(ImgLabel label, Mat image)
    {
        Rect roi = new(label.RangeX, label.RangeY, label.RangeWidth, label.RangeHeight);
        if (roi.X < 0 || roi.Y < 0 || roi.Width <= 0 || roi.Height <= 0 ||
            (long)roi.X + roi.Width > image.Width || (long)roi.Y + roi.Height > image.Height)
            throw new InvalidDataException($"标签 [{label.name}] ROI 超出输入图像");
        using Mat range = new(image, roi);
        using Mat template = Cv2.ImDecode(Convert.FromBase64String(label.ImgBase64),
            label.searchMethod == SearchMethod.MaskedSqDiffNormed ? ImreadModes.Unchanged : ImreadModes.Color);
        if (template.Empty() || template.Width > roi.Width || template.Height > roi.Height)
            throw new InvalidDataException($"标签 [{label.name}] 模板为空或大于 ROI");
        using Mat result = new();
        if (label.searchMethod == SearchMethod.MaskedSqDiffNormed)
        {
            if (template.Channels() != 4) throw new InvalidDataException("透明匹配需要四通道模板");
            Mat[] channels = Cv2.Split(template);
            try
            {
                if (Cv2.CountNonZero(channels[3]) == 0) throw new InvalidDataException("模板完全透明");
                using Mat color = new();
                Cv2.Merge([channels[0], channels[1], channels[2]], color);
                Cv2.MatchTemplate(range, color, result, TemplateMatchModes.SqDiffNormed, channels[3]);
            }
            finally { foreach (Mat channel in channels) channel.Dispose(); }
        }
        else if (label.searchMethod is SearchMethod.EdgeDetectXY or SearchMethod.EdgeDetectLaplacian)
        {
            using Mat sourceEdges = OpenCVSearch.EdgeDetect(range, label.searchMethod);
            using Mat templateEdges = OpenCVSearch.EdgeDetect(template, label.searchMethod);
            Cv2.MatchTemplate(sourceEdges, templateEdges, result, TemplateMatchModes.CCoeffNormed);
        }
        else
        {
            if (label.searchMethod is not (SearchMethod.SqDiffNormed or SearchMethod.CCorrNormed or SearchMethod.CCoeffNormed))
                throw new NotSupportedException($"标签 [{label.name}] 算法 {label.searchMethod} 不支持兼容识别");
            Cv2.MatchTemplate(range, template, result, (TemplateMatchModes)(int)label.searchMethod);
        }
        Cv2.MinMaxLoc(result, out double min, out double max, out Point minLocation, out Point maxLocation);
        bool difference = label.searchMethod is SearchMethod.SqDiffNormed or SearchMethod.MaskedSqDiffNormed;
        Point location = difference ? minLocation : maxLocation;
        RawMatchResult match = new(difference ? min : max,
            new Rect(roi.X + location.X, roi.Y + location.Y, template.Width, template.Height), label.searchMethod);
        _ = LegacyAlgorithmAdapter.ToLegacyFloat(match);
        return match;
    }
}