namespace EasyCon.Core.Capabilities;

/// <summary>
/// 宿主 = 能力实例集合；null = 不可用（对应脚本函数得到"不支持"语义）。
/// 属性可变：GUI/CLI 宿主在运行期逐项装配能力。
/// 能力模型纯属 PC 装配层：VM 核（EcxInterpreter / ecs_host / ecs_vm.c）不感知本类型。
/// </summary>
public sealed class CapabilitySet
{
    /// <summary>L1 键鼠摇杆（WAIT/KEY/KEYST/STICK/STICKC/AMIIBO）。</summary>
    public IPadInput? Input { get; set; }

    /// <summary>PRINT/ALERT。</summary>
    public IConsoleIo? Console { get; set; }

    /// <summary>ARG/APP。</summary>
    public IHostEnvironment? Environment { get; set; }

    /// <summary>文件族（FOpen/FRead/FWrite/...）。</summary>
    public IFileSystem? Files { get; set; }

    /// <summary>截屏帧（FRAME）。</summary>
    public ICaptureSource? Capture { get; set; }

    /// <summary>图像处理/标签匹配（IMG/ROI）。</summary>
    public IVisionService? Vision { get; set; }

    /// <summary>OCR（后端可换）。</summary>
    public IOcrService? Ocr { get; set; }

    /// <summary>ONNX 通用推理（P5）。</summary>
    public IInference? Inference { get; set; }
}