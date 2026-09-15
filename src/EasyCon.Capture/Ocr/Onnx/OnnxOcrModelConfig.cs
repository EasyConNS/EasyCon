using System.Text.Json;

namespace EasyCon.Capture.Ocr.Onnx;

/// <summary>
/// OpenCV DNN/ONNX OCR 模型清单。相对路径以清单所在目录为基准。
/// </summary>
public sealed class OnnxOcrModelConfig
{
    /// <summary>文字识别 ONNX 模型路径。</summary>
    public string RecognitionModel { get; set; } = string.Empty;

    /// <summary>CTC 字符词典路径，每行对应一个类别文本。</summary>
    public string CharacterDictionary { get; set; } = string.Empty;

    /// <summary>可选的文字检测 ONNX 模型路径。</summary>
    public string DetectionModel { get; set; } = string.Empty;

    /// <summary>识别模型预处理和解码参数。</summary>
    public RecOptions Recognition { get; set; } = new();

    /// <summary>检测模型后处理参数。</summary>
    public DetOptions Detection { get; set; } = new();

    internal static OnnxOcrModelConfig Load(string path)
    {
        using FileStream stream = File.OpenRead(path);
        var config = JsonSerializer.Deserialize<OnnxOcrModelConfig>(stream, SerializerOptions)
            ?? throw new InvalidDataException($"Invalid ONNX OCR config: {path}");
        config.Validate(path);
        return config;
    }

    internal void Validate(string source)
    {
        if (Detection == null)
            throw new InvalidDataException($"Detection options are required: {source}");
        if (Detection.MaxSideLen <= 0 || Detection.InputMultiple <= 0)
            throw new InvalidDataException($"Invalid detection input dimensions: {source}");
        if (!float.IsFinite(Detection.BoxThreshold) || Detection.BoxThreshold is < 0 or > 1)
            throw new InvalidDataException($"Detection BoxThreshold must be between 0 and 1: {source}");
        if (!double.IsFinite(Detection.Scale))
            throw new InvalidDataException($"Detection Scale must be finite: {source}");
        if (Detection.Mean == null || Detection.Mean.Length != 3
            || Detection.Mean.Any(value => !double.IsFinite(value)))
            throw new InvalidDataException($"Detection Mean must contain exactly three values: {source}");
        if (Detection.StandardDeviation == null || Detection.StandardDeviation.Length != 3
            || Detection.StandardDeviation.Any(value => !double.IsFinite(value) || value <= 0))
            throw new InvalidDataException($"Detection StandardDeviation must contain three positive values: {source}");
        if (Recognition == null)
            throw new InvalidDataException($"Recognition options are required: {source}");
        if (Recognition.ImageHeight <= 0)
            throw new InvalidDataException($"ImageHeight must be positive: {source}");
        if (Recognition.MinImageWidth <= 0 || Recognition.MaxImageWidth < Recognition.MinImageWidth)
            throw new InvalidDataException($"Invalid recognition width range: {source}");
        if (Recognition.WidthMultiple <= 0)
            throw new InvalidDataException($"WidthMultiple must be positive: {source}");
        if (Recognition.Mean == null || Recognition.Mean.Length != 3
            || Recognition.Mean.Any(value => !double.IsFinite(value)))
            throw new InvalidDataException($"Mean must contain exactly three values: {source}");
        if (!double.IsFinite(Recognition.Scale))
            throw new InvalidDataException($"Scale must be finite: {source}");
        if (!float.IsFinite(Recognition.MinScore) || Recognition.MinScore is < 0 or > 1)
            throw new InvalidDataException($"MinScore must be between 0 and 1: {source}");
        if (Recognition.BlankIndex < 0)
            throw new InvalidDataException($"BlankIndex cannot be negative: {source}");
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}