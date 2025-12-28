using Tesseract;

namespace EasyCon.Capture;

internal class OCRDetect(string lang = "chi_sim", EngineMode engineMode = EngineMode.Default, PageSegMode pageSegMode = PageSegMode.SingleLine)
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
    public string TesserDetect(MemoryStream stream, out float confidence)
    {
        using var img = Pix.LoadFromMemory(stream.ToArray());
        return TesserDetect(img, out confidence);
    }

    public string TesserDetect(Pix img, out float confidence)
    {
        using var engine = new TesseractEngine(tessdataPath, lang, engineMode);
        using var page = engine.Process(img, pageSegMode);
        confidence = page.GetMeanConfidence();
        return page.GetText();
    }
}
