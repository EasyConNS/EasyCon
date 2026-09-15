using EasyCon.Capture;
using EasyCon.Capture.Ocr;

namespace EasyCon.Core.Capabilities;

/// <summary>Tesseract 后端 <see cref="IOcrService"/>（包 EzTesseract，经 <see cref="OcrEngineCache"/> 按语言缓存）。</summary>
public sealed class TesseractOcrService(OcrEngineCache? cache = null)
    : EngineCacheOcrService(new TesseractEngineFactory(), cache)
{
    public override string Backend => "tesseract";
}