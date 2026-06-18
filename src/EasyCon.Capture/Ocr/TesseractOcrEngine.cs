using EzTesseract;
using EzTesseract.Enums;

namespace EasyCon.Capture.Ocr;

/// <summary>
/// Tesseract OCR 识别器 — 将 EzTesseract.Engine 包装为 IOcrRecognizer。
/// 对应 ocr-rs 的 RecOnlyEngine 层：输入已裁剪的文字行图片，输出文本。
/// 生命周期：Dispose 时释放内部 Tesseract 引擎。
/// </summary>
public sealed class TesseractRecognizer : IOcrRecognizer
{
    private readonly Engine _engine;
    private readonly PageSegMode _psm;

    internal TesseractRecognizer(Engine engine, PageSegMode psm)
    {
        _engine = engine;
        _psm = psm;
    }

    /// <inheritdoc/>
    public OcrRecognizeResult Recognize(byte[] image)
    {
        using var pix = EzTesseract.Pix.Image.LoadFromMemory(image);
        using var page = _engine.Process(pix, _psm);
        return new OcrRecognizeResult(page.Text, page.MeanConfidence);
    }

    public void Dispose()
    {
        _engine.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Tesseract 端到端 OCR 引擎 — 包装 EzTesseract.Engine。
/// 对应 ocr-rs 的 OcrEngine 层。
/// 注意：Tesseract 不区分检测/识别阶段，Detect() 抛出 NotSupportedException，
/// Detector / Recognizer 返回 null（需分离使用时请直接创建 TesseractRecognizer）。
/// </summary>
public sealed class TesseractOcrEngine : IOcrEngine
{
    private readonly Engine _engine;
    private readonly PageSegMode _psm;

    internal TesseractOcrEngine(Engine engine, PageSegMode psm)
    {
        _engine = engine;
        _psm = psm;
    }

    /// <inheritdoc/>
    /// <remarks>Tesseract 不支持返回检测框，结果的 Box 为 null。</remarks>
    public OcrResult[] Recognize(byte[] image)
    {
        using var pix = EzTesseract.Pix.Image.LoadFromMemory(image);
        using var page = _engine.Process(pix, _psm);
        return [new OcrResult(page.Text, page.MeanConfidence, null)];
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Tesseract 当前绑定不支持分离检测。</exception>
    public OcrTextBox[] Detect(byte[] image)
    {
        throw new NotSupportedException("Tesseract engine does not support separate text detection.");
    }

    /// <inheritdoc/>
    public IOcrDetector? Detector => null;

    /// <inheritdoc/>
    public IOcrRecognizer? Recognizer => null;

    public void Dispose()
    {
        _engine.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Tesseract 引擎工厂 — 创建 Tesseract 识别器 / 引擎。
/// 实现 IOcrEngineFactory，供 OcrEngineCache 插件式使用。
/// </summary>
public sealed class TesseractEngineFactory : IOcrEngineFactory
{
    /// <inheritdoc/>
    public IOcrRecognizer CreateRecognizer(string lang, string dataPath, string engineMode, string psmode)
    {
        var em = ParseEngineMode(engineMode);
        var psm = ParsePageSegMode(psmode);
        var engine = new Engine(dataPath, lang, em);
        return new TesseractRecognizer(engine, psm);
    }

    /// <inheritdoc/>
    public IOcrEngine? CreateEngine(string lang, string dataPath, string engineMode, string psmode)
    {
        var em = ParseEngineMode(engineMode);
        var psm = ParsePageSegMode(psmode);
        var engine = new Engine(dataPath, lang, em);
        return new TesseractOcrEngine(engine, psm);
    }

    internal static EngineMode ParseEngineMode(string mode) => mode.ToUpperInvariant() switch
    {
        "LSTM_ONLY" => EngineMode.LstmOnly,
        "LEGACY_ONLY" => EngineMode.TesseractOnly,
        _ => EngineMode.Default,
    };

    internal static PageSegMode ParsePageSegMode(string mode) => mode.ToUpperInvariant() switch
    {
        "AUTO" => PageSegMode.Auto,
        "AUTO_OSD" => PageSegMode.AutoOsd,
        "BLOCK" => PageSegMode.SingleBlock,
        "SINGLE_BLOCK" => PageSegMode.SingleBlock,
        "SINGLE_CHAR" => PageSegMode.SingleChar,
        "SINGLE_COLUMN" => PageSegMode.SingleColumn,
        "SINGLE_WORD" => PageSegMode.SingleWord,
        _ => PageSegMode.SingleLine,
    };
}