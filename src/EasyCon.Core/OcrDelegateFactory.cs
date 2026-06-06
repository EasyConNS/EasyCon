using EasyCon.Capture;
using EasyScript;
using OpenCvSharp;
using TesseractOCR.Enums;

namespace EasyCon.Core;

public static class OcrDelegateFactory
{
    /// <summary>
    /// 创建 OcrInitDelegate：初始化并缓存 Tesseract 引擎。
    /// </summary>
    public static OcrInitDelegate CreateInit(OcrEngineCache cache)
    {
        return (lang, dataPath, engineMode, psmode) =>
            cache.Init(lang, dataPath, engineMode, psmode);
    }

    /// <summary>
    /// 创建 OcrDelegate：优先复用缓存引擎，未命中时 per-call 兜底。
    /// </summary>
    public static OcrDelegate Create(Func<Mat> frameProvider, OcrEngineCache cache, string fallbackDataPath)
    {
        return (x, y, w, h, lang) =>
        {
            using var frame = frameProvider?.Invoke();
            if (frame == null || frame.Empty()) return "OCR NOT SUPPORT";

            x = Math.Clamp(x, 0, frame.Width);
            y = Math.Clamp(y, 0, frame.Height);
            w = Math.Clamp(w, 0, frame.Width - x);
            h = Math.Clamp(h, 0, frame.Height - y);

            if (w == 0 || h == 0) return "OCR ARGS ERR!";

            using var roi = new Mat(frame, new Rect(x, y, w, h));
            using var ms = new MemoryStream(roi.ToPngBytes());
            using var img = TesseractOCR.Pix.Image.LoadFromMemory(ms.ToArray());

            // 优先查缓存
            var cached = cache.TryGet(lang);
            if (cached != null)
            {
                var text = OCRDetect.TesserDetect(cached.Value.Engine, img, cached.Value.Psm, out var conf);
                cache.LastConfidence = (int)(conf * 100);
                return text;
            }

            // Per-call 兜底
            var fallbackText = OCRDetect.TesserDetect(img, out var fallbackConf, lang, fallbackDataPath);
            cache.LastConfidence = (int)(fallbackConf * 100);
            return fallbackText;
        };
    }
}

public static class FrameDelegateFactory
{
    public static FrameDelegate CreateFrame(Func<Mat> frameProvider)
    {
        return (x, y, w, h) =>
        {
            using var mat = frameProvider?.Invoke();
            if (mat == null || mat.Empty()) return "采集卡检查异常";
            if (x >= 0 && y >= 0 && w >= 0 && h >= 0)
            {
                x = Math.Clamp(x, 0, mat.Width);
                y = Math.Clamp(y, 0, mat.Height);
                w = Math.Clamp(w, 0, mat.Width - x);
                h = Math.Clamp(h, 0, mat.Height - y);

                using var roi = new Mat(mat, new Rect(x, y, w, h));
                if (w == 0 || h == 0) return "ROI检查异常";
                return Convert.ToBase64String(roi.ToPngBytes());
            }
            return Convert.ToBase64String(mat.ToPngBytes());
        };
    }
}