// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming

namespace EzCv.Dnn;

/// <summary>
/// Enum of computation backends supported by layers.
/// </summary>
/// <remarks>
/// DNN_BACKEND_DEFAULT equals to DNN_BACKEND_INFERENCE_ENGINE if
/// OpenCV is built with Intel's Inference Engine library or
/// DNN_BACKEND_OPENCV otherwise.
/// </remarks>
public enum Backend
{
    /// <summary>
    /// DNN_BACKEND_DEFAULT equals to DNN_BACKEND_INFERENCE_ENGINE if
    /// OpenCV is built with Intel's Inference Engine library or
    /// DNN_BACKEND_OPENCV otherwise.
    /// </summary>
    DEFAULT = 0,

    /// <summary>Intel OpenVINO computational backend.</summary>
    INFERENCE_ENGINE = 2,

    /// <summary>OpenCV built-in backend.</summary>
    OPENCV = 3,

    /// <summary>VKCOM backend.</summary>
    VKCOM = 5,

    /// <summary>CUDA backend.</summary>
    CUDA = 6,

    /// <summary>WebNN backend.</summary>
    WEBNN = 7,

    /// <summary>TIM-VX backend.</summary>
    TIMVX = 8,

    /// <summary>Huawei CANN backend.</summary>
    CANN = 9,
}