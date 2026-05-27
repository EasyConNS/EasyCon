using EasyCon.Capture;
using EasyScript;
using OpenCvSharp;

namespace EasyCon.Core;

public static class OcrDelegateFactory
{
    public static OcrDelegate Create(Func<Mat> frameProvider)
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
            return OCRDetect.TesserDetect(ms, out _, lang);
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