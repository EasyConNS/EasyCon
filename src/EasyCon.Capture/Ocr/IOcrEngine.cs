namespace EasyCon.Capture.Ocr;

/// <summary>
/// 文本检测引擎接口 — 从图片中定位文字区域。
/// 对应 ocr-rs 的 DetModel / DetOnlyEngine 层。
/// </summary>
public interface IOcrDetector : IDisposable
{
    /// <summary>从图片中检测所有文字区域的位置。</summary>
    /// <param name="image">PNG 编码的图片字节。</param>
    /// <returns>检测到的文本框列表。</returns>
    OcrTextBox[] Detect(byte[] image);
}

/// <summary>
/// 文本识别引擎接口 — 从已裁剪的文字行图片中识别文本。
/// 对应 ocr-rs 的 RecModel / RecOnlyEngine 层。
/// </summary>
public interface IOcrRecognizer : IDisposable
{
    /// <summary>识别单张文字行图片中的文本。</summary>
    /// <param name="image">PNG 编码的图片字节（已裁剪到文字行）。</param>
    /// <returns>识别结果（文本 + 置信度）。</returns>
    OcrRecognizeResult Recognize(byte[] image);
}

/// <summary>
/// 端到端 OCR 引擎接口 — 检测 + 识别一体化。
/// 对应 ocr-rs 的 OcrEngine 层。
/// </summary>
public interface IOcrEngine : IDisposable
{
    /// <summary>
    /// 端到端识别：检测图片中所有文字区域并识别文本。
    /// </summary>
    /// <param name="image">PNG 编码的图片字节。</param>
    /// <returns>识别结果列表（每个结果包含文本、置信度、检测框）。</returns>
    OcrResult[] Recognize(byte[] image);

    /// <summary>
    /// 仅检测：定位图片中所有文字区域，不做识别。
    /// </summary>
    /// <param name="image">PNG 编码的图片字节。</param>
    /// <returns>检测到的文本框列表。</returns>
    OcrTextBox[] Detect(byte[] image);

    /// <summary>底层检测器（如果引擎支持分离检测）。</summary>
    IOcrDetector? Detector { get; }

    /// <summary>底层识别器（如果引擎支持分离识别）。</summary>
    IOcrRecognizer? Recognizer { get; }
}

/// <summary>
/// OCR 引擎工厂接口 — 插件式创建引擎实例。
/// 引入此接口后，OcrEngineCache 不再依赖具体引擎类型。
/// </summary>
public interface IOcrEngineFactory
{
    /// <summary>创建识别器实例。</summary>
    /// <param name="lang">语言代码，如 "chi_sim"、"eng"。</param>
    /// <param name="dataPath">模型 / 数据目录路径。</param>
    /// <param name="engineMode">引擎模式字符串（引擎特定）。</param>
    /// <param name="psmode">页面分割模式字符串（引擎特定）。</param>
    IOcrRecognizer CreateRecognizer(string lang, string dataPath, string engineMode, string psmode);

    /// <summary>创建端到端引擎实例（可选，如果引擎仅支持分离模式则返回 null）。</summary>
    IOcrEngine? CreateEngine(string lang, string dataPath, string engineMode, string psmode);
}