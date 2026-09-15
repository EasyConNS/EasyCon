using EasyCon.Capture.Ocr;
using EasyCon.Capture.Ocr.Onnx;

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

    private readonly IOcrEngineFactory? _factory;
    private readonly Dictionary<string, CachedEntry> _engines = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 创建引擎缓存。
    /// </summary>
    /// <param name="factory">可选的固定引擎工厂；不提供时根据 engineMode 选择 Tesseract 或 ONNX。</param>
    public OcrEngineCache(IOcrEngineFactory? factory = null)
    {
        _factory = factory;
    }

    /// <summary>最近一次 OCR 调用的置信度 (0~100)</summary>
    public int LastConfidence { get; set; }

    /// <summary>最近一次初始化失败的原因；成功后清空。</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// 默认 tessdata / 模型目录路径。
    /// 当 GetOrInit 触发自动初始化时使用此路径。
    /// </summary>
    public string DefaultDataPath { get; set; } = string.Empty;

    /// <summary>自动初始化时使用的引擎模式。</summary>
    public string DefaultEngineMode { get; set; } = "DEFAULT";

    /// <summary>自动初始化时使用的页面模式或 ONNX 配置文件。</summary>
    public string DefaultPsmode { get; set; } = "SINGLE_LINE";

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
            {
                LastError = null;
                return true; // 完全匹配，跳过
            }
        }

        try
        {
            IOcrEngineFactory factory = _factory ?? CreateFactory(dataPath, engineMode);
            var engine = factory.CreateRecognizer(lang, dataPath, engineMode, psmode);
            if (existing != null) existing.Engine.Dispose();
            _engines[lang] = new CachedEntry(engine, dataPath, engineMode, psmode);
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
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
    public IOcrRecognizer GetOrInit(string lang)
    {
        if (_engines.TryGetValue(lang, out var cached))
            return cached.Engine;

        if (!Init(lang, DefaultDataPath, DefaultEngineMode, DefaultPsmode))
            throw new InvalidOperationException(LastError ?? $"Failed to initialize OCR language '{lang}'.");
        return _engines[lang].Engine;
    }

    private static IOcrEngineFactory CreateFactory(string dataPath, string engineMode)
    {
        bool isOnnxMode = engineMode.Equals("ONNX", StringComparison.OrdinalIgnoreCase)
            || engineMode.StartsWith("ONNX:", StringComparison.OrdinalIgnoreCase);
        if (!isOnnxMode)
            return new TesseractEngineFactory();

        GpuBackend backend = ParseOnnxBackend(engineMode);
        return new OnnxEngineFactory(dataPath, backend: backend);
    }

    internal static GpuBackend ParseOnnxBackend(string engineMode)
    {
        string[] parts = engineMode.Split(':', 2, StringSplitOptions.TrimEntries);
        if (!parts[0].Equals("ONNX", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Not an ONNX engine mode: {engineMode}", nameof(engineMode));
        if (parts.Length == 1 || string.IsNullOrWhiteSpace(parts[1])) return GpuBackend.Cpu;
        if (Enum.TryParse(parts[1], true, out GpuBackend backend) && Enum.IsDefined(backend)) return backend;
        throw new ArgumentException($"Unknown ONNX backend '{parts[1]}'.", nameof(engineMode));
    }

    public void Dispose()
    {
        foreach (var cached in _engines.Values)
            cached.Engine.Dispose();
        _engines.Clear();
    }
}