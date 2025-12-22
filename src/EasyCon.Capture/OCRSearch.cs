using Tesseract;

namespace EasyCapture;

abstract class AbstractSearch { }

internal class OCRSearch : AbstractSearch
{
    const string tessdataPath = @"./Tessdata";
    public static float TesserDetect(MemoryStream stream, out string result)
    {
        using var img = Pix.LoadFromMemory(stream.ToArray());
        return TesserDetect(img, out result);
    }

    public static float TesserDetect(Pix img, out string result, string lang = "chi_sim")
    {
        using var engine = new TesseractEngine(tessdataPath, lang, EngineMode.Default);
        using var page = engine.Process(img, PageSegMode.SingleLine);
        result = page.GetText();
        return page.GetMeanConfidence();
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
