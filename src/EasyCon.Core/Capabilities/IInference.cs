namespace EasyCon.Core.Capabilities;

/// <summary>
/// ONNX 通用推理服务（P5 落地 NET_LOAD/NET_RUN 实验函数的宿主后端，经 EzCv.Dnn 多后端执行）。
/// 模型文件为外部资产，不入库。
/// </summary>
public interface IInference : IDisposable
{
    /// <summary>加载 ONNX 模型，返回会话句柄；失败返回 -1。</summary>
    int Load(string modelPath);

    /// <summary>执行推理：输入/输出为扁平 float 张量；失败返回 null。</summary>
    float[]? Run(int session, float[] input);

    /// <summary>
    /// 以显式形状持有输入张量（如 [1,3,320,320]），返回张量句柄；失败返回 -1。
    /// 大数组驻留宿主侧，脚本只见句柄（原生边界纯标量协议）。
    /// </summary>
    int HoldTensor(float[] data, int[] shape);

    /// <summary>释放张量句柄；句柄无效时静默。</summary>
    void FreeTensor(int handle);

    /// <summary>
    /// 以持有的张量为输入执行推理；成功返回输出扁平数组（失败 null）。
    /// 与 <see cref="Run"/> 的差异仅在输入来源（句柄 vs 数组）。
    /// </summary>
    float[]? RunHeld(int session, int tensorHandle);

    /// <summary>按 (scale, offset) 逐元素变换张量（t2 = t·scale + offset），返回新句柄；失败 -1。</summary>
    int TransformTensor(int handle, float scale, float offset);

    /// <summary>卸载会话（句柄失效）。</summary>
    void Unload(int session);

    /// <summary>
    /// 最近一次 Run/RunHeld 的输出形状（如 [1,T,C]）；无输出历史时为 null。
    /// 供 NET_ARGMAX/NET_ROWS 做按行解码。
    /// </summary>
    int[]? LastOutputShape { get; }
}