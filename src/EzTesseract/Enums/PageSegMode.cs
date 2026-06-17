namespace EzTesseract.Enums;

/// <summary>
/// 页面分割模式（PSM）。值与 libtesseract capi 完全一致。
/// </summary>
public enum PageSegMode
{
    /// <summary>仅方向与脚本检测（OSD）。</summary>
    OsdOnly = 0,

    /// <summary>自动分割 + OSD。</summary>
    AutoOsd = 1,

    /// <summary>自动分割，不做 OSD/OCR。</summary>
    AutoOnly = 2,

    /// <summary>全自动分割，不做 OSD。</summary>
    Auto = 3,

    /// <summary>假设单列文本。</summary>
    SingleColumn = 4,

    /// <summary>假设单个垂直对齐的文本块。</summary>
    SingleBlockVertText = 5,

    /// <summary>假设单个统一文本块。</summary>
    SingleBlock = 6,

    /// <summary>视为单行文本。</summary>
    SingleLine = 7,

    /// <summary>视为单个单词。</summary>
    SingleWord = 8,

    /// <summary>视为圆形中的单个单词。</summary>
    CircleWord = 9,

    /// <summary>视为单个字符。</summary>
    SingleChar = 10,

    /// <summary>稀疏文本。</summary>
    SparseText = 11,

    /// <summary>稀疏文本 + OSD。</summary>
    SparseTextOsd = 12,

    /// <summary>视为单行，跳过 tesseract 特定 hack。</summary>
    RawLine = 13,
}
