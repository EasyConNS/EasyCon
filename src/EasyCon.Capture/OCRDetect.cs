using EzTesseract;
using EzTesseract.Enums;

namespace EasyCon.Capture;

public sealed class OCRDetect
{
    /// <summary>
    /// 使用外部传入的 Engine 实例执行 OCR（不创建不释放 Engine）。
    /// </summary>
    public static string TesserDetect(Engine engine, EzTesseract.Pix.Image img, PageSegMode psm, out float confidence)
    {
        using var page = engine.Process(img, psm);
        confidence = page.MeanConfidence;
        return page.Text;
    }

    /// <summary>
    /// 按需创建 Engine 执行 OCR（per-call 模式，用于未缓存的情况）。
    /// </summary>
    public static string TesserDetect(MemoryStream stream, out float confidence, string lang, string dataPath, string engineMode = "DEFAULT", string psmode = "SINGLE_LINE")
    {
        using var img = EzTesseract.Pix.Image.LoadFromMemory(stream.ToArray());
        return TesserDetect(img, out confidence, lang, dataPath, engineMode, psmode);
    }

    /// <summary>
    /// 按需创建 Engine 执行 OCR（从 Pix.Image）。
    /// </summary>
    public static string TesserDetect(EzTesseract.Pix.Image img, out float confidence, string lang, string dataPath, string engineMode = "DEFAULT", string psmode = "SINGLE_LINE")
    {
        var em = engineMode.ToUpperInvariant() switch
        {
            "LSTM_ONLY" => EngineMode.LstmOnly,
            "LEGACY_ONLY" => EngineMode.TesseractOnly,
            _ => EngineMode.Default,
        };
        var psm = psmode.ToUpperInvariant() switch
        {
            "AUTO" => PageSegMode.Auto,
            "BLOCK" => PageSegMode.SingleBlock,
            _ => PageSegMode.SingleLine,
        };

        using var engine = new Engine(dataPath, lang, em);
        using var page = engine.Process(img, psm);
        confidence = page.MeanConfidence;
        return page.Text.Trim('\n');
    }
}
