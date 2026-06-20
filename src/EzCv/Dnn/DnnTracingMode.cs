// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming

namespace EzCv.Dnn;

/// <summary>
/// 追踪模式
/// </summary>
public enum TracingMode
{
    /// <summary>不追踪任何内容</summary>
    TRACE_NONE = 0,

    /// <summary>打印所有执行的操作以及输出张量，与 ONNX Runtime 兼容</summary>
    TRACE_ALL = 1,

    /// <summary>打印所有执行的操作，输入输出的类型和形状会被打印，但内容不会</summary>
    TRACE_OP = 2,
}
