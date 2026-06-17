namespace EzTesseract.Enums;

/// <summary>
/// Tesseract 引擎模式（OEM）。值与 libtesseract capi 完全一致。
/// </summary>
public enum EngineMode
{
    /// <summary>仅使用传统 tesseract 引擎。</summary>
    TesseractOnly = 0,

    /// <summary>仅使用 LSTM 引擎。</summary>
    LstmOnly = 1,

    /// <summary>同时使用传统与 LSTM 引擎。</summary>
    TesseractAndLstm = 2,

    /// <summary>默认引擎（当前为 LSTM）。与 TesseractOCR 的 EngineMode.Default 对齐。</summary>
    Default = 3,
}
