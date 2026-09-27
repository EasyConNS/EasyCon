using EasyCon.Capture;
using EasyCon.Capture.Ocr;
using EasyScript;
using OpenCvSharp;

namespace EasyCon.Core;

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