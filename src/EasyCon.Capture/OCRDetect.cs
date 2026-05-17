using Tesseract;

namespace EasyCon.Capture;

public sealed class OCRDetect
{
    const string tessdataPath = @"./Tessdata";

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
        using var img = Pix.LoadFromMemory(stream.ToArray());
        return TesserDetect(img, out confidence, lang);
    }

    public static string TesserDetect(Pix img, out float confidence, string lang)
    {
        using var engine = new TesseractEngine(tessdataPath, lang, EngineMode.Default);
        using var page = engine.Process(img, PageSegMode.SingleLine);
        confidence = page.GetMeanConfidence();
        return page.GetText();
    }
}