namespace EasyCon.Core.Capabilities;

/// <summary>
/// OCR 引擎具名配置（替代旧 OCR_INIT 位置参数签名）。
/// 旧签名映射：lang → Language，dataPath → ModelPath，
/// engineMode/psmode → Options["tess:engineMode"]/Options["tess:psmode"]。
/// </summary>
public sealed class OcrConfig
{
    public string Language { get; init; } = "";

    /// <summary>模型/ tessdata 目录；null = 宿主默认。</summary>
    public string? ModelPath { get; init; }

    public IReadOnlyDictionary<string, string> Options { get; init; } =
        new Dictionary<string, string>();
}