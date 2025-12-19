using Tesseract;

namespace EasyCapture;

abstract class AbstractSearch { }

internal class OCRSearch : AbstractSearch
{
    const string tessdataPath = @"./Tessdata";
    public static float TesserDetect(MemoryStream stream, out string result)
    {
        using var img = Pix.LoadFromMemory(stream.ToArray());
        return TesserDetect(img, out result);
    }

    public static float TesserDetect(Pix img, out string result, string lang = "chi_sim")
    {
        using var engine = new TesseractEngine(tessdataPath, lang, EngineMode.Default);
        using var page = engine.Process(img, PageSegMode.SingleLine);
        result = page.GetText();
        return page.GetMeanConfidence();
    }
}
