using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace EasyCon.Core.Capabilities;

/// <summary>
/// OpenCvSharp.Dnn 通用 ONNX 推理 <see cref="IInference"/>（NET_LOAD/NET_RUN 实验函数的宿主后端，P5）。
/// 实验面约定：输入/输出为一维 float（NCHW 展平，输入以 1×1×1×N blob 送入，
/// 仅适配向量输入模型；卷积模型须自行展平并接受形状不匹配的失败语义）。
/// 模型为外部资产，不入库。Load/Run 失败返回 -1 / null，不抛异常。
/// </summary>
public sealed class DnnInference : IInference
{
    readonly Dictionary<int, Net> _nets = new();
    readonly object _gate = new();
    int _nextSession;
    bool _disposed;

    public int Load(string modelPath)
    {
        try
        {
            Net net = Cv2.Dnn.ReadNetFromONNX(modelPath);
            if (net == null || net.Empty())
            {
                net?.Dispose();
                return -1;
            }
            lock (_gate)
            {
                int session = ++_nextSession;
                _nets[session] = net;
                return session;
            }
        }
        catch
        {
            return -1;
        }
    }

    public float[]? Run(int session, float[] input)
    {
        Net? net;
        lock (_gate)
        {
            if (_disposed || !_nets.TryGetValue(session, out net))
                return null;
        }

        try
        {
            // 1×N 向量输入：从托管数组复制进 Mat，避免固定输入数组
            using var blob = new Mat(1, input.Length, MatType.CV_32FC1);
            MarshalCopy(input, blob);
            net.SetInput(blob);
            using var output = net.Forward();
            return Flatten(output);
        }
        catch
        {
            return null;
        }
    }

    public void Unload(int session)
    {
        lock (_gate)
        {
            if (_nets.Remove(session, out var net))
                net.Dispose();
        }
    }

    static unsafe void MarshalCopy(float[] source, Mat target)
    {
        fixed (float* p = source)
        {
            Buffer.MemoryCopy((void*)target.Data, p, sizeof(float) * source.Length, sizeof(float) * source.Length);
        }
    }

    static float[] Flatten(Mat output)
    {
        int dims = output.Dims;
        int total = 1;
        for (int i = 0; i < dims; i++)
            total *= output.Size(i);
        var result = new float[total];
        if (total > 0 && output.ElemSize() == sizeof(float))
        {
            unsafe
            {
                float* p = (float*)output.Data;
                for (int i = 0; i < total; i++)
                    result[i] = p[i];
            }
        }
        return result;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var net in _nets.Values)
                net.Dispose();
            _nets.Clear();
            _disposed = true;
        }
    }
}