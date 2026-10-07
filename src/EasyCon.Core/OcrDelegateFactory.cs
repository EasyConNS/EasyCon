using EasyCon.Capture;
using EasyScript;
using OpenCvSharp;

namespace EasyCon.Core;

public static class FrameDelegateFactory
{
    /// <summary>无帧哨兵：旧委托契约以字符串传达失败（新端口语义下归一为 null）。</summary>
    public const string NoFrameError = "采集卡检查异常";

    /// <summary>ROI 非法哨兵（旧契约；新端口语义下归一为 null）。</summary>
    public const string RoiError = "ROI检查异常";

    public static FrameDelegate CreateFrame(Func<FrameLease?> frameProvider)
    {
        return (x, y, w, h) =>
        {
            using var lease = frameProvider?.Invoke();
            return CropToBase64(lease?.Mat, x, y, w, h) ?? NoFrameError;
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
            return CropToBase64(mat, x, y, w, h) ?? NoFrameError;
        };
    }

    /// <summary>
    /// 帧 → Base64 PNG：x/y/w/h 全非负时裁 ROI（越界按帧边界收敛），否则整帧。
    /// 无帧或 ROI 收敛到空返回 <c>null</c>——失败哨兵只由旧委托入口
    /// （<see cref="CreateFrame(Func{FrameLease?})"/>）负责转换，跨端口调用方拿 null。
    /// </summary>
    public static string? CropToBase64(Mat? mat, int x, int y, int w, int h)
    {
        if (mat == null || mat.Empty()) return null;
        if (x < 0 || y < 0 || w < 0 || h < 0)
            return Convert.ToBase64String(mat.ToBytes(".png"));

        x = Math.Clamp(x, 0, mat.Width);
        y = Math.Clamp(y, 0, mat.Height);
        w = Math.Clamp(w, 0, mat.Width - x);
        h = Math.Clamp(h, 0, mat.Height - y);
        if (w == 0 || h == 0) return null;

        using var roi = new Mat(mat, new Rect(x, y, w, h));
        return Convert.ToBase64String(roi.ToBytes(".png"));
    }
}