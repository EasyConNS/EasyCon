namespace EasyCon.Capture.Ocr;

/// <summary>
/// OCR 引擎通用配置。
/// 对应 ocr-rs 的 OcrEngineConfig。
/// </summary>
public sealed class OcrEngineConfig
{
    /// <summary>推理线程数（默认 4）。</summary>
    public int Threads { get; set; } = 4;

    /// <summary>GPU 后端类型（默认 CPU）。</summary>
    public GpuBackend Backend { get; set; } = GpuBackend.Cpu;

    /// <summary>检测配置（仅支持检测的引擎使用）。</summary>
    public DetOptions? DetOptions { get; set; }

    /// <summary>识别配置。</summary>
    public RecOptions? RecOptions { get; set; }

    /// <summary>快速模式预设。</summary>
    public static OcrEngineConfig Fast() => new()
    {
        Threads = 2,
        RecOptions = new RecOptions { MinScore = 0.3f },
    };

    /// <summary>精确模式预设。</summary>
    public static OcrEngineConfig Precise() => new()
    {
        Threads = 8,
        RecOptions = new RecOptions { MinScore = 0.7f },
    };

    /// <summary>GPU 加速预设。</summary>
    public static OcrEngineConfig Gpu(GpuBackend backend = GpuBackend.Metal) => new()
    {
        Backend = backend,
    };
}

/// <summary>
/// GPU 后端类型。对应 ocr-rs 的 Backend 枚举。
/// </summary>
public enum GpuBackend
{
    /// <summary>纯 CPU 推理。</summary>
    Cpu = 0,

    /// <summary>Apple Metal（macOS/iOS）。</summary>
    Metal,

    /// <summary>跨平台 OpenCL。</summary>
    OpenCL,

    /// <summary>Vulkan。</summary>
    Vulkan,

    /// <summary>NVIDIA CUDA。</summary>
    Cuda,

    /// <summary>Apple CoreML。</summary>
    CoreML,
}

/// <summary>
/// 检测模型配置。对应 ocr-rs 的 DetOptions。
/// </summary>
public sealed class DetOptions
{
    /// <summary>最大边长（默认 960）。</summary>
    public int MaxSideLen { get; set; } = 960;

    /// <summary>检测框阈值 (0.0 ~ 1.0，默认 0.3)。</summary>
    public float BoxThreshold { get; set; } = 0.3f;

    /// <summary>是否合并相邻文本框。</summary>
    public bool MergeBoxes { get; set; } = true;
}

/// <summary>
/// 识别模型配置。对应 ocr-rs 的 RecOptions。
/// </summary>
public sealed class RecOptions
{
    /// <summary>最低置信度阈值 (0.0 ~ 1.0，默认 0.0 不过滤)。</summary>
    public float MinScore { get; set; } = 0f;

    /// <summary>批处理大小（默认 6）。</summary>
    public int BatchSize { get; set; } = 6;
}

/// <summary>
/// GpuBackend → OpenCV DNN Backend / Target 映射。
/// </summary>
public static class OnnxProviderMapper
{
    /// <summary>
    /// 根据 GpuBackend 映射到 OpenCV DNN 的 Backend 和 Target。
    /// </summary>
    public static (EzCv.Dnn.Backend backend, EzCv.Dnn.Target target) MapBackend(GpuBackend backend)
    {
        return backend switch
        {
            GpuBackend.Cuda => (EzCv.Dnn.Backend.CUDA, EzCv.Dnn.Target.CUDA),
            GpuBackend.OpenCL => (EzCv.Dnn.Backend.DEFAULT, EzCv.Dnn.Target.OPENCL),
            GpuBackend.Vulkan => (EzCv.Dnn.Backend.DEFAULT, EzCv.Dnn.Target.VULKAN),
            GpuBackend.Metal or GpuBackend.CoreML => (EzCv.Dnn.Backend.DEFAULT, EzCv.Dnn.Target.CPU),
            _ => (EzCv.Dnn.Backend.DEFAULT, EzCv.Dnn.Target.CPU),
        };
    }
}