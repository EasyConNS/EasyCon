using EasyCon.Capture;
using EasyCon.Capture.Ocr;
using EasyScript;
using EzCv;

namespace EasyCon.Core;

public static class OcrDelegateFactory
{
    /// <summary>
    /// 创建 OcrInitDelegate：初始化并缓存 OCR 引擎。
    /// </summary>
    public static OcrInitDelegate CreateInit(OcrEngineCache cache)
    {
        return (lang, dataPath, engineMode, psmode) =>
            cache.Init(lang, dataPath, engineMode, psmode);
    }

    /// <summary>
    /// 创建 OcrDelegate：从缓存获取或自动初始化引擎，执行 OCR 识别。
    /// 不再有 per-call 兜底逻辑——引擎始终通过 OcrEngineCache 管理生命周期。
    /// </summary>
    public static OcrDelegate Create(Func<FrameLease?> frameProvider, OcrEngineCache cache)
    {
        return (x, y, w, h, lang) =>
        {
            using var lease = frameProvider?.Invoke();
            if (lease == null || lease.Mat.Empty()) return "OCR NOT SUPPORT";
            var frame = lease.Mat;

            x = Math.Clamp(x, 0, frame.Width);
            y = Math.Clamp(y, 0, frame.Height);
            w = Math.Clamp(w, 0, frame.Width - x);
            h = Math.Clamp(h, 0, frame.Height - y);

            if (w == 0 || h == 0) return "OCR ARGS ERR!";

            using var roi = new Mat(frame, new Rect(x, y, w, h));
            var imageBytes = roi.ToBytes(".png");

            var recognizer = cache.GetOrInit(lang);
            var result = recognizer.Recognize(imageBytes);
            cache.LastConfidence = (int)(result.Confidence * 100);
            return result.Text;
        };
    }

    /// <summary>
    /// 兼容重载：接收返回自有 Mat 的提供者（旧式 WPF 仍按此方式调用）。
    /// </summary>
    public static OcrDelegate Create(Func<Mat> frameProvider, OcrEngineCache cache)
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
            var imageBytes = roi.ToBytes(".png");

            var recognizer = cache.GetOrInit(lang);
            var result = recognizer.Recognize(imageBytes);
            cache.LastConfidence = (int)(result.Confidence * 100);
            return result.Text;
        };
    }
}

public static class FrameDelegateFactory
{
    public static FrameDelegate CreateFrame(Func<FrameLease?> frameProvider)
    {
        return (x, y, w, h) =>
        {
            using var lease = frameProvider?.Invoke();
            if (lease == null || lease.Mat.Empty()) return "采集卡检查异常";
            var mat = lease.Mat;
            if (x >= 0 && y >= 0 && w >= 0 && h >= 0)
            {
                x = Math.Clamp(x, 0, mat.Width);
                y = Math.Clamp(y, 0, mat.Height);
                w = Math.Clamp(w, 0, mat.Width - x);
                h = Math.Clamp(h, 0, mat.Height - y);

                using var roi = new Mat(mat, new Rect(x, y, w, h));
                if (w == 0 || h == 0) return "ROI检查异常";
                return Convert.ToBase64String(roi.ToBytes(".png"));
            }
            return Convert.ToBase64String(mat.ToBytes(".png"));
        };
    }

    /// <summary>
    /// 兼容重载：接收返回自有 Mat 的提供者（旧式 WPF 仍按此方式调用）。
    /// </summary>
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
                return Convert.ToBase64String(roi.ToBytes(".png"));
            }
            return Convert.ToBase64String(mat.ToBytes(".png"));
        };
    }
}