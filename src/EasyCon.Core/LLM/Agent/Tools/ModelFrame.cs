using EasyCon.Core.Capabilities;
using OpenCvSharp;

namespace EasyCon.Core.LLM.Agent.Tools;

/// <summary>
/// 模型侧帧编码：把能力端口产出的 PNG 帧转成对模型省 token 的半分辨率 JPEG。
/// 端口契约（D4）固定 Base64 PNG 全帧；面向模型的压缩是工具层表现策略，
/// 不反向写进能力接口。解码失败时原样返回 PNG（上层按"有帧"继续，宁可多花 token 不可瞎）。
/// </summary>
internal static class ModelFrame
{
    /// <summary>
    /// 取帧类工具唯一入口：整帧捕获（语义化 <see cref="ICaptureSource.CaptureFullFrame"/>）→
    /// 首帧等待（连接后立即取帧的时序竞争，共约 2 秒有界重试）→ 模型侧压缩。
    /// 无帧返回 null，由调用方决定错误文案；OCR 类工具直接走 CaptureFrame/CaptureFullFrame，
    /// 不要用本方法（压缩伤识别率）。
    /// </summary>
    public static async Task<string?> CaptureForModelAsync(ICaptureSource source, CancellationToken ct = default)
    {
        var png = source.CaptureFullFrame();
        for (var attempt = 0; string.IsNullOrEmpty(png) && attempt < 9; attempt++)
        {
            try
            {
                await Task.Delay(200, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            png = source.CaptureFullFrame();
        }
        return string.IsNullOrEmpty(png) ? null : EncodeForModel(png);
    }

    public static string EncodeForModel(string pngBase64)
    {
        try
        {
            using var mat = Mat.FromImageData(Convert.FromBase64String(pngBase64));
            if (mat.Empty())
                return pngBase64;

            using var resized = new Mat();
            Cv2.Resize(mat, resized, new Size(Math.Max(1, mat.Width / 2), Math.Max(1, mat.Height / 2)),
                0, 0, InterpolationFlags.Linear);
            return Convert.ToBase64String(
                resized.ToBytes(".jpg", [new ImageEncodingParam(ImwriteFlags.JpegQuality, 80)]));
        }
        catch
        {
            return pngBase64;
        }
    }
}