using EasyCon.Capture;
using EasyScript;
using OpenCvSharp;

namespace EasyCon.Core;

public static class OcrDelegateFactory
{
    public static OcrDelegate Create(Func<Mat?> frameProvider)
    {
        return (x, y, w, h, lang) =>
        {
            using var frame = frameProvider();
            if (frame == null || frame.Empty()) return "OCR NOT SUPPORT";

            x = Math.Clamp(x, 0, frame.Width);
            y = Math.Clamp(y, 0, frame.Height);
            w = Math.Clamp(w, 0, frame.Width - x);
            h = Math.Clamp(h, 0, frame.Height - y);

            if (w == 0 || h == 0) return "OCR ARGS ERR!";

            using var roi = new Mat(frame, new Rect(x, y, w, h));
            Cv2.ImEncode(".png", roi, out var bytes);
            using var ms = new MemoryStream(bytes);
            return OCRDetect.TesserDetect(ms, out _, lang);
        };
    }
}
