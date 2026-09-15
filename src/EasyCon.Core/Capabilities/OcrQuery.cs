namespace EasyCon.Core.Capabilities;

/// <summary>识别请求：区域坐标相对传入的 <see cref="ImageRef"/>（X=Y=Width=Height=0 表示整图）。</summary>
public sealed class OcrQuery
{
    /// <summary>识别语言（脚本 OCR 第 5 参；null = 引擎默认）。</summary>
    public string? Language { get; init; }

    public int X { get; init; }

    public int Y { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }
}