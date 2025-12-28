using OpenCvSharp;
using System.Diagnostics;

namespace EasyCon.Capture;

internal static class MatchFacts
{
    /// <summary>
    /// 简化的字符串匹配度计算，使用编辑距离方法
    /// </summary>
    public static Point MatchTemplate(Mat big, Mat small, SearchMethod method, out double matchDegree)
    {
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

        using var result = new Mat();
        Cv2.MatchTemplate(big, small, result, tmplMatchMode);
        Cv2.MinMaxLoc(result, out double min, out double max, out var minLoc, out var maxLoc);

        Debug.WriteLine($"{method}[min:{min:f1}, max:{max:f1}]");
        switch (method)
        {
            case SearchMethod.SqDiff:
            case SearchMethod.SqDiffNormed:
                matchDegree = (1 - min) / 1.0;
                break;
            case SearchMethod.CCorr:
            case SearchMethod.CCorrNormed:
                matchDegree = max / 1.0;
                break;
            case SearchMethod.CCoeff:
            case SearchMethod.CCoeffNormed:
            default:
                matchDegree = (max + 1) / 2.0;
                break;
        }

        // the sqD lower is good
        if (method == SearchMethod.SqDiff || method == SearchMethod.SqDiffNormed)
        {
            return minLoc;
        }
        else
        {
            return maxLoc;
        }
    }

    /// <summary>
    /// 简化的字符串匹配度计算，使用编辑距离方法
    /// </summary>
    /// <param name="str1">要比较的第一个字符串</param>
    /// <param name="str2">要比较的第二个字符串</param>
    /// <returns>匹配度（0~1之间）</returns>
    public static double StringMatchSimple(string str1, string str2)
    {
        // 如果两个字符串都为空，匹配度为1
        if (string.IsNullOrEmpty(str1) && string.IsNullOrEmpty(str2))
        {
            return 1.0;
        }

        // 如果有一个为空，匹配度为0
        if (string.IsNullOrEmpty(str1) || string.IsNullOrEmpty(str2))
        {
            return 0.0;
        }

        // 如果完全相同，直接返回1
        if (str1 == str2)
        {
            return 1.0;
        }

        int len1 = str1.Length;
        int len2 = str2.Length;

        // 创建动态规划表
        int[,] dp = new int[len1 + 1, len2 + 1];

        // 初始化
        for (int i = 0; i <= len1; i++)
        {
            dp[i, 0] = i;
        }
        for (int j = 0; j <= len2; j++)
        {
            dp[0, j] = j;
        }

        // 计算编辑距离
        for (int i = 1; i <= len1; i++)
        {
            for (int j = 1; j <= len2; j++)
            {
                int cost = (str1[i - 1] == str2[j - 1]) ? 0 : 1;
                dp[i, j] = Math.Min(
                    Math.Min(
                        dp[i - 1, j] + 1,      // 删除
                        dp[i, j - 1] + 1       // 插入
                    ),
                    dp[i - 1, j - 1] + cost    // 替换
                );
            }
        }

        // 计算匹配度
        int maxLen = Math.Max(len1, len2);
        double similarity = 1.0 - (double)dp[len1, len2] / maxLen;

        return Math.Max(similarity, 0.0);  // 确保结果在0~1之间
    }
}
