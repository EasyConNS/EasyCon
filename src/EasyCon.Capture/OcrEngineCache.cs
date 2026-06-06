using TesseractOCR;
using TesseractOCR.Enums;

namespace EasyCon.Capture;

/// <summary>
/// OCR 引擎缓存：按 lang 缓存 Tesseract Engine 实例，支持幂等初始化和置信度传递。
/// </summary>
public sealed class OcrEngineCache : IDisposable
{
    private sealed record CachedEntry(Engine Engine, PageSegMode Psm, string DataPath, string EngineModeStr, string PsmStr);

    private readonly Dictionary<string, CachedEntry> _engines = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>最近一次 OCR 调用的置信度 (0~100)</summary>
    public int LastConfidence { get; set; }

    /// <summary>
    /// 初始化并缓存指定语言的引擎。相同参数时跳过（幂等），不同参数时替换。
    /// </summary>
    public bool Init(string lang, string dataPath, string engineMode, string psmode)
    {
        if (_engines.TryGetValue(lang, out var existing))
        {
            if (existing.DataPath == dataPath
                && existing.EngineModeStr == engineMode
                && existing.PsmStr == psmode)
                return true; // 完全匹配，跳过

            existing.Engine.Dispose();
            _engines.Remove(lang);
        }

        try
        {
            var em = ParseEngineMode(engineMode);
            var psm = ParsePsmode(psmode);
            var engine = new Engine(dataPath, lang, em);
            _engines[lang] = new CachedEntry(engine, psm, dataPath, engineMode, psmode);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 尝试获取缓存引擎。命中返回 Engine + PSM，未命中返回 null。
    /// </summary>
    public (Engine Engine, PageSegMode Psm)? TryGet(string lang)
    {
        if (_engines.TryGetValue(lang, out var cached))
            return (cached.Engine, cached.Psm);
        return null;
    }

    public void Dispose()
    {
        foreach (var cached in _engines.Values)
            cached.Engine.Dispose();
        _engines.Clear();
    }

    private static EngineMode ParseEngineMode(string mode) => mode.ToUpperInvariant() switch
    {
        "LSTM_ONLY" => EngineMode.LstmOnly,
        "LEGACY_ONLY" => EngineMode.TesseractOnly,
        _ => EngineMode.Default,
    };

    private static PageSegMode ParsePsmode(string mode) => mode.ToUpperInvariant() switch
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