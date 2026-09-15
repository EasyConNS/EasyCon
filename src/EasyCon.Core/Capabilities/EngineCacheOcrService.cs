using EasyCon.Capture;
using EasyCon.Capture.Ocr;
using OpenCvSharp;

namespace EasyCon.Core.Capabilities;

/// <summary>
/// 「区域裁剪 + 引擎缓存识别」共享骨架（<see cref="OcrEngineCache"/> 后端，Tesseract / Paddle-ONNX 复用）：
/// Recognize 的 query 区域相对传入 image（Width/Height &lt;= 0 表示整图）；
/// 裁剪/置信度语义与旧 OcrDelegateFactory 逐项一致，供新旧路径对拍。
/// </summary>
public abstract class EngineCacheOcrService(IOcrEngineFactory defaultFactory, OcrEngineCache? cache = null) : IOcrService
{
    /// <summary>按语言缓存的识别器（幂等 Init，不同参数时替换）。</summary>
    protected readonly OcrEngineCache Cache = cache ?? new OcrEngineCache(defaultFactory);

    public abstract string Backend { get; }

    public int LastConfidence => Cache.LastConfidence;

    public bool Init(OcrConfig cfg)
    {
        var dataPath = cfg.ModelPath ?? Cache.DefaultDataPath;
        return Cache.Init(
            cfg.Language,
            dataPath,
            cfg.Options.GetValueOrDefault("tess:engineMode", "DEFAULT"),
            cfg.Options.GetValueOrDefault("tess:psmode", "SINGLE_LINE"));
    }

    public string Recognize(ImageRef image, OcrQuery query)
    {
        try
        {
            using var mat = Mat.FromImageData(Convert.FromBase64String(image.Base64));
            if (mat.Empty())
                return "";

            int x = Math.Clamp(query.X, 0, mat.Width);
            int y = Math.Clamp(query.Y, 0, mat.Height);
            int w = query.Width > 0 ? Math.Clamp(query.Width, 0, mat.Width - x) : mat.Width - x;
            int h = query.Height > 0 ? Math.Clamp(query.Height, 0, mat.Height - y) : mat.Height - y;
            if (w == 0 || h == 0)
                return "OCR ARGS ERR!";

            using var roi = new Mat(mat, new Rect(x, y, w, h));
            var imageBytes = roi.ToBytes(".png");

            var recognizer = Cache.GetOrInit(query.Language ?? "");
            var result = recognizer.Recognize(imageBytes);
            Cache.LastConfidence = (int)(result.Confidence * 100);
            return result.Text;
        }
        catch
        {
            return "";
        }
    }

    public void Dispose() => Cache.Dispose();
}