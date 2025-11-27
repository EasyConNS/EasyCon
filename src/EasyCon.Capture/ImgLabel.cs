using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyCapture;

/// <summary>
/// 表示图像处理的目标区域
/// </summary>
public struct ImageRegion
{
    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    public ImageRegion(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }
}

/// <summary>
/// 图像处理类型枚举
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProcessingType
{
    /// <summary> 灰度直方图对比 </summary>
    [JsonPropertyName("histogram")]
    GrayscaleHistogram,

    /// <summary> 结构相似性指数 </summary>
    [JsonPropertyName("ssim")]
    SSIM,

    /// <summary> 感知哈希 </summary>
    [JsonPropertyName("phash")]
    PerceptualHash,

    /// <summary> 余弦相似度 </summary>
    [JsonPropertyName("cosine")]
    CosineSimilarity,

    /// <summary> Jaccard相似系数 </summary>
    [JsonPropertyName("jaccard")]
    JaccardIndex,

    /// <summary> 编辑距离 </summary>
    [JsonPropertyName("levenshtein")]
    LevenshteinDistance
}

/// <summary>
/// 图像处理格式定义
/// </summary>
public class ImageProcessingFormat
{
    /// <summary>
    /// 目标区域坐标和尺寸
    /// </summary>
    [JsonPropertyName("region")]
    public ImageRegion TargetRegion { get; set; }

    /// <summary>
    /// 图像数据（Base64编码）或OCR文本
    /// </summary>
    [JsonPropertyName("data")]
    public string TargetData { get; set; }

    /// <summary>
    /// 处理方式（根据数据类型自动选择）
    /// </summary>
    [JsonPropertyName("method")]
    public ProcessingType ProcessingMethod { get; set; }

    /// <summary>
    /// 可接受的相似度阈值 (0-1)
    /// </summary>
    [JsonPropertyName("threshold")]
    public double SimilarityThreshold
    {
        get => _similarityThreshold;
        set => _similarityThreshold = Math.Clamp(value, 0.0, 1.0);
    }
    private double _similarityThreshold = 0.95;

    /// <summary>
    /// 检查当前处理方式是否适用于图像数据
    /// </summary>
    public bool IsImageProcessingMethod()
    {
        return ProcessingMethod switch
        {
            ProcessingType.GrayscaleHistogram => true,
            ProcessingType.SSIM => true,
            ProcessingType.PerceptualHash => true,
            _ => false
        };
    }

    /// <summary>
    /// 检查当前处理方式是否适用于文本数据
    /// </summary>
    public bool IsTextProcessingMethod()
    {
        return ProcessingMethod switch
        {
            ProcessingType.CosineSimilarity => true,
            ProcessingType.JaccardIndex => true,
            ProcessingType.LevenshteinDistance => true,
            _ => false
        };
    }

    /// <summary>
    /// 验证配置的合理性
    /// </summary>
    public bool Validate(out string errorMessage)
    {
        if (TargetRegion.Width <= 0 || TargetRegion.Height <= 0)
        {
            errorMessage = "目标区域尺寸无效";
            return false;
        }

        if (string.IsNullOrWhiteSpace(TargetData))
        {
            errorMessage = "目标数据不能为空";
            return false;
        }

        // 检查处理方式与数据类型的匹配
        if (IsImageData && !IsImageProcessingMethod())
        {
            errorMessage = $"处理方式'{ProcessingMethod}'不适用于图像数据";
            return false;
        }

        if (!IsImageData && !IsTextProcessingMethod())
        {
            errorMessage = $"处理方式'{ProcessingMethod}'不适用于文本数据";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    /// <summary>
    /// 保存配置到.il文件
    /// </summary>
    /// <param name="filePath">完整文件路径，自动添加.il后缀</param>
    public void SaveToFile(string filePath)
    {
        // 确保文件后缀为.il
        if (!filePath.EndsWith(".il", StringComparison.OrdinalIgnoreCase))
        {
            filePath += ".il";
        }

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        string json = JsonSerializer.Serialize(this, options);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// 从.il文件加载配置
    /// </summary>
    /// <param name="filePath">完整文件路径</param>
    public static ImageProcessingFormat LoadFromFile(string filePath)
    {
        // 检查文件后缀
        if (!filePath.EndsWith(".il", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("仅支持.il格式文件", nameof(filePath));
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("配置文件不存在", filePath);
        }

        string json = File.ReadAllText(filePath);
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() }
        };

        return JsonSerializer.Deserialize<ImageProcessingFormat>(json, options)
               ?? throw new InvalidDataException("配置文件格式无效");
    }

    /// <summary>
    /// 检查是否为图像数据（Base64编码）
    /// </summary>
    [JsonIgnore]
    public bool IsImageData => IsBase64String(TargetData);

    /// <summary>
    /// 高效检查字符串是否为有效的Base64格式
    /// </summary>
    private static bool IsBase64String(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        if (s.Length % 4 != 0) return false;

        try
        {
            // 尝试转换验证
            _ = Convert.FromBase64String(s);
            return true;
        }
        catch
        {
            return false;
        }
    }
}