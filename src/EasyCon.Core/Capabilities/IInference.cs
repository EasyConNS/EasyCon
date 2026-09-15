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

    /// <summary>卸载会话（句柄失效）。</summary>
    void Unload(int session);
}