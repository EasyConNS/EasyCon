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
            LastOutputShape = ShapeOf(output);
            return Flatten(output);
        }
        catch
        {
            LastOutputShape = null;
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

    // ---- 输出形状跟踪（NET_ARGMAX / NET_ROWS 按行解码）----

    /// <inheritdoc />
    public int[]? LastOutputShape { get; private set; }

    static int[] ShapeOf(Mat m)
    {
        var shape = new int[m.Dims];
        for (int i = 0; i < m.Dims; i++)
            shape[i] = m.Size(i);
        return shape;
    }

    // ---- 句柄协议（NET_IMAGE / NET_RUNH / NET_SCALE / NET_FREE）：大数组驻留宿主侧 ----

    readonly Dictionary<int, (float[] Data, int[] Shape)> _tensors = new();
    int _nextTensor;

    /// <inheritdoc />
    public int HoldTensor(float[] data, int[] shape)
    {
        if (data.Length == 0 || shape.Length == 0)
            return -1;
        long total = 1;
        foreach (var dim in shape)
        {
            if (dim <= 0) return -1;
            total *= dim;
        }
        if (total != data.Length)
            return -1;

        lock (_gate)
        {
            if (_disposed) return -1;
            int handle = ++_nextTensor;
            _tensors[handle] = (data, shape);
            return handle;
        }
    }

    /// <inheritdoc />
    public void FreeTensor(int handle)
    {
        lock (_gate)
            _tensors.Remove(handle);
    }

    /// <inheritdoc />
    public float[]? RunHeld(int session, int tensorHandle)
    {
        Net? net;
        float[] data;
        int[] shape;
        lock (_gate)
        {
            if (_disposed
                || !_nets.TryGetValue(session, out net)
                || !_tensors.TryGetValue(tensorHandle, out var tensor))
                return null;
            data = tensor.Data;
            shape = tensor.Shape;
        }

        try
        {
            using var blob = Mat.FromPixelData(shape, MatType.CV_32FC1, data);
            net.SetInput(blob);
            using var output = net.Forward();
            LastOutputShape = ShapeOf(output);
            return Flatten(output);
        }
        catch
        {
            LastOutputShape = null;
            return null;
        }
    }

    /// <summary>按 (scale, offset) 逐元素变换张量，返回新句柄（原张量不变）。</summary>
    public int TransformTensor(int handle, float scale, float offset)
    {
        lock (_gate)
        {
            if (_disposed || !_tensors.TryGetValue(handle, out var tensor))
                return -1;
            var result = new float[tensor.Data.Length];
            for (int i = 0; i < tensor.Data.Length; i++)
                result[i] = tensor.Data[i] * scale + offset;
            int newHandle = ++_nextTensor;
            _tensors[newHandle] = (result, tensor.Shape);
            return newHandle;
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