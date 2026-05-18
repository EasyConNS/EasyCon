using TesseractOCR;
using TesseractOCR.Enums;

namespace EasyCon.Capture;

public sealed class OCRDetect
{
    static readonly string tessdataPath = AppDomain.CurrentDomain.BaseDirectory + "\\Tessdata\\";

    //private readonly string lang = language;
    //private readonly EngineMode egMode = engineMode;
    //private readonly PageSegMode psMode = pageSegMode;

    /// <summary>
    /// language: trained tessdata
    /// enginMod: EngineMode.Default
    /// pageSegMod: PageSegMode.SingleLine
    /// </summary>
    public static string TesserDetect(MemoryStream stream, out float confidence, string lang = "chi_sim")
    {
        using var img = TesseractOCR.Pix.Image.LoadFromMemory(stream.ToArray());
        return TesserDetect(img, out confidence, lang);
    }

    public static string TesserDetect(TesseractOCR.Pix.Image img, out float confidence, string lang)
    {
        using var engine = new Engine(tessdataPath, lang, EngineMode.Default);
        using var page = engine.Process(img, PageSegMode.SingleLine);
        confidence = page.MeanConfidence;
        return page.Text.Trim('\n');
    }
}