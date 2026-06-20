namespace EzCv.Dnn;

/// <summary>
/// DNN 引擎类型（对应 cv::dnn::EngineType，OpenCV 5 新增）。
/// 用于控制模型加载时使用的推理引擎。
/// </summary>
public enum DnnEngineType
{
    /// <summary>强制使用旧引擎（类似 OpenCV 4.x 行为）。</summary>
    CLASSIC = 1,

    /// <summary>强制使用新引擎（当前仅支持 CPU 后端）。</summary>
    NEW = 2,

    /// <summary>自动选择：先尝试新引擎，失败则回退旧引擎（默认）。</summary>
    AUTO = 3,

    /// <summary>使用 ONNX Runtime（需编译时启用 WITH_ONNXRUNTIME=ON）。</summary>
    ORT = 4,
}
