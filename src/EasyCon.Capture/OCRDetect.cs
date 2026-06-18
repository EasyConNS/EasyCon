using EasyCon.Capture.Ocr;
using EzTesseract;
using EzTesseract.Enums;

namespace EasyCon.Capture;

/// <summary>
/// OCR 识别门面 — 提供静态便捷方法，基于 IOcrRecognizer 接口。
/// 不再支持 per-call 创建/销毁引擎的模式（引擎生命周期由 OcrEngineCache 统一管理）。
/// </summary>
public sealed class OCRDetect
{
    /// <summary>
    /// 使用 IOcrRecognizer 执行 OCR（引擎由调用方管理生命周期）。
    /// </summary>
    public static string Recognize(IOcrRecognizer recognizer, byte[] imagePng, out float confidence)
    {
        var result = recognizer.Recognize(imagePng);
        confidence = result.Confidence;
        return result.Text;
    }

    /// <summary>
    /// 使用外部传入的 Tesseract Engine 实例直接执行 OCR。
    /// 保留此方法用于需要直接操作 Tesseract 底层引擎的场景。
    /// </summary>
    public static string TesserDetect(Engine engine, EzTesseract.Pix.Image img, PageSegMode psm, out float confidence)
    {
        using var page = engine.Process(img, psm);
        confidence = page.MeanConfidence;
        return page.Text;
    }
}