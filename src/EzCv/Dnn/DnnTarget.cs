// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming

namespace EzCv.Dnn;

/// <summary>
/// Enum of target devices for computations.
/// </summary>
public enum Target
{
    CPU = 0,
    OPENCL = 1,
    OPENCL_FP16 = 2,
    MYRIAD = 3,
    VULKAN = 4,

    /// <summary>
    /// FPGA device with CPU fallbacks using Inference Engine's Heterogeneous plugin.
    /// </summary>
    FPGA = 5,
    CUDA = 6,
    CUDA_FP16 = 7,
    HDDL = 8,
    NPU = 9,

    /// <summary>Only the ARM platform is supported. Low precision computing, accelerate model inference.</summary>
    CPU_FP16 = 10,
}
