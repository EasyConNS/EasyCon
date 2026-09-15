namespace EasyCon.Core.Capabilities;

/// <summary>
/// OCR 服务（后端可换：tesseract / paddle-onnx）。
/// 引擎选择是宿主装配决策，不进脚本语法——脚本只见 OCR(x,y,w,h,lang) / OCR_INIT。
/// </summary>
public interface IOcrService : IDisposable
{
    /// <summary>后端标识（"tesseract" | "paddle-onnx" | "delegate"）。</summary>
    string Backend { get; }

    /// <summary>按具名配置初始化（幂等，重复调用以已初始化引擎为准）；失败返回 false。</summary>
    bool Init(OcrConfig cfg);

    /// <summary>识别 image 的 query 区域文本；失败返回空串。</summary>
    string Recognize(ImageRef image, OcrQuery query);

    /// <summary>最近一次识别的置信度（OCR_CONF syscall 语义；无识别历史返回 0）。</summary>
    int LastConfidence { get; }
}