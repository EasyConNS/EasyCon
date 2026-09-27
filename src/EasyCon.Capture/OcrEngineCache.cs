using EasyCon.Capture.Ocr;

namespace EasyCon.Capture;

/// <summary>
/// OCR 引擎缓存：按 lang 缓存 IOcrRecognizer 实例，支持幂等初始化和置信度传递。
/// 通过 IOcrEngineFactory 实现引擎类型无关，可切换 Tesseract / ONNX 等后端。
/// 
/// 默认使用 Tesseract 引擎，DefaultDataPath 用于自动初始化未缓存的引擎。
/// </summary>
public sealed class OcrEngineCache : IDisposable
{
    private sealed record CachedEntry(IOcrRecognizer Engine, string DataPath, string EngineMode, string Psmode);

    private readonly IOcrEngineFactory _factory;
    private readonly Dictionary<string, CachedEntry> _engines = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 创建引擎缓存。
    /// </summary>
    /// <param name="factory">引擎工厂（默认 Tesseract）。</param>
    public OcrEngineCache(IOcrEngineFactory? factory = null)
    {
        _factory = factory ?? new TesseractEngineFactory();
    }

    /// <summary>最近一次 OCR 调用的置信度 (0~100)</summary>
    public int LastConfidence { get; set; }

    /// <summary>最近一次 Init 失败的原因（成功初始化后清空）。排障锚点，勿吞。</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// 默认 tessdata / 模型目录路径。
    /// 当 GetOrInit 触发自动初始化时使用此路径。
    /// </summary>
    public string DefaultDataPath { get; set; } = string.Empty;

    /// <summary>
    /// 初始化并缓存指定语言的引擎。相同参数时跳过（幂等），不同参数时替换。
    /// </summary>
    public bool Init(string lang, string dataPath, string engineMode, string psmode)
    {
        if (_engines.TryGetValue(lang, out var existing))
        {
            if (existing.DataPath == dataPath
                && existing.EngineMode == engineMode
                && existing.Psmode == psmode)
                return true; // 完全匹配，跳过

            existing.Engine.Dispose();
            _engines.Remove(lang);
        }

        try
        {
            var engine = _factory.CreateRecognizer(lang, dataPath, engineMode, psmode);
            _engines[lang] = new CachedEntry(engine, dataPath, engineMode, psmode);
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            // 保留原因供 GetOrInit 抛出明确异常：这里若静默返回 false，
            // 调用方拿到的将是误导性的 KeyNotFoundException
            LastError = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// 尝试获取缓存识别器。命中返回 IOcrRecognizer，未命中返回 null。
    /// </summary>
    public IOcrRecognizer? TryGet(string lang)
    {
        if (_engines.TryGetValue(lang, out var cached))
            return cached.Engine;
        return null;
    }

    /// <summary>
    /// 获取或自动初始化识别器：缓存命中直接返回，未命中则用默认参数自动创建并缓存。
    /// 默认参数：DefaultDataPath、DEFAULT 引擎模式、SINGLE_LINE 页面分割。
    /// </summary>
    /// <exception cref="InvalidOperationException">引擎初始化失败（原因见消息与 <see cref="LastError"/>）。</exception>
    public IOcrRecognizer GetOrInit(string lang)
    {
        if (_engines.TryGetValue(lang, out var cached))
            return cached.Engine;
        return GetOrInit(lang, DefaultDataPath, "DEFAULT", "SINGLE_LINE");
    }

    /// <summary>
    /// 获取或按显式参数初始化识别器；参数与缓存一致时跳过重建。
    /// </summary>
    /// <exception cref="InvalidOperationException">引擎初始化失败（原因见消息与 <see cref="LastError"/>）。</exception>
    public IOcrRecognizer GetOrInit(string lang, string dataPath, string engineMode, string psmode)
    {
        if (_engines.TryGetValue(lang, out var cached)
            && cached.DataPath == dataPath
            && cached.EngineMode == engineMode
            && cached.Psmode == psmode)
            return cached.Engine;

        if (!Init(lang, dataPath, engineMode, psmode))
            throw new InvalidOperationException(
                $"OCR 引擎初始化失败 (lang={lang}, dataPath={dataPath}, engine={engineMode}, psm={psmode}): {LastError}");
        return _engines[lang].Engine;
    }

    public void Dispose()
    {
        foreach (var cached in _engines.Values)
            cached.Engine.Dispose();
        _engines.Clear();
    }
}