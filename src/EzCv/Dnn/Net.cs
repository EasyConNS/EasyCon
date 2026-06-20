using EzCv.Interop;

namespace EzCv.Dnn;

/// <summary>
/// DNN 神经网络，API 兼容 OpenCvSharp.Dnn.Net。
/// 内部持有 ezcv_native 的 cv::dnn::Net 句柄，实现 IDisposable。
/// </summary>
public class Net : IDisposable
{
    internal IntPtr Handle { get; private set; }

    private bool _disposed;

    internal Net(IntPtr handle)
    {
        Handle = handle;
    }

    // =========================================================================
    // 静态工厂
    // =========================================================================

    /// <summary>
    /// 自动检测格式加载网络模型。
    /// </summary>
    /// <param name="model">二进制权重文件路径（.onnx / .pb / .bin 等）。</param>
    /// <param name="config">文本配置文件路径（.pbtxt / .xml 等，可选）。</param>
    /// <param name="framework">显式框架名称（可选）。</param>
    /// <param name="engine">DNN 引擎类型（默认 Auto）。</param>
    public static Net ReadNet(string model, string config = "", string framework = "",
        DnnEngineType engine = DnnEngineType.AUTO)
    {
        var h = EzCvDll.DnnReadNet(model, config, framework, (int)engine);
        EzCvError.ThrowIfAny();
        return new Net(h);
    }

    /// <summary>
    /// 从 ONNX 文件加载网络。
    /// </summary>
    /// <param name="onnxFile">ONNX 模型文件路径。</param>
    /// <param name="engine">DNN 引擎类型（默认 Auto）。</param>
    public static Net ReadNetFromONNX(string onnxFile, DnnEngineType engine = DnnEngineType.AUTO)
    {
        var h = EzCvDll.DnnReadNetFromOnnx(onnxFile, (int)engine);
        EzCvError.ThrowIfAny();
        return new Net(h);
    }

    /// <summary>
    /// 从内存字节数组加载 ONNX 网络。
    /// </summary>
    /// <param name="onnxFileData">ONNX 模型字节数据。</param>
    /// <param name="engine">DNN 引擎类型（默认 Auto）。</param>
    public static Net ReadNetFromONNX(byte[] onnxFileData, DnnEngineType engine = DnnEngineType.AUTO)
    {
        unsafe
        {
            fixed (byte* p = onnxFileData)
            {
                var h = EzCvDll.DnnReadNetFromOnnxMem(p, onnxFileData.Length, (int)engine);
                EzCvError.ThrowIfAny();
                return new Net(h);
            }
        }
    }

    /// <summary>
    /// 从 TensorFlow 模型文件加载网络。
    /// </summary>
    /// <param name="model">.pb 模型文件路径。</param>
    /// <param name="config">.pbtxt 配置文件路径（可选）。</param>
    /// <param name="engine">DNN 引擎类型（默认 Auto）。</param>
    public static Net ReadNetFromTensorflow(string model, string config = "", DnnEngineType engine = DnnEngineType.AUTO)
    {
        IntPtr h;
        if (string.IsNullOrEmpty(config))
        {
            h = EzCvDll.DnnReadNetFromTensorflow(model, (int)engine);
        }
        else
        {
            h = EzCvDll.DnnReadNetFromTensorflowWithConfig(model, config, (int)engine);
        }
        EzCvError.ThrowIfAny();
        return new Net(h);
    }

    /// <summary>
    /// 从内存字节数组加载 TensorFlow 网络。
    /// </summary>
    /// <param name="bufferModel">.pb 模型字节数据。</param>
    /// <param name="bufferConfig">.pbtxt 配置字节数据（可选）。</param>
    /// <param name="engine">DNN 引擎类型（默认 Auto）。</param>
    public static Net ReadNetFromTensorflow(byte[] bufferModel, byte[]? bufferConfig = null, DnnEngineType engine = DnnEngineType.AUTO)
    {
        unsafe
        {
            fixed (byte* pModel = bufferModel)
            {
                if (bufferConfig == null || bufferConfig.Length == 0)
                {
                    var h = EzCvDll.DnnReadNetFromTensorflowMem(pModel, bufferModel.Length, null, 0, (int)engine);
                    EzCvError.ThrowIfAny();
                    return new Net(h);
                }
                else
                {
                    fixed (byte* pConfig = bufferConfig)
                    {
                        var h = EzCvDll.DnnReadNetFromTensorflowMem(pModel, bufferModel.Length, pConfig, bufferConfig.Length, (int)engine);
                        EzCvError.ThrowIfAny();
                        return new Net(h);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 从 TFLite 模型文件加载网络。
    /// </summary>
    /// <param name="model">.tflite 模型文件路径。</param>
    /// <param name="engine">DNN 引擎类型（默认 Auto）。</param>
    public static Net ReadNetFromTFLite(string model, DnnEngineType engine = DnnEngineType.AUTO)
    {
        var h = EzCvDll.DnnReadNetFromTflite(model, (int)engine);
        EzCvError.ThrowIfAny();
        return new Net(h);
    }

    /// <summary>
    /// 从内存字节数组加载 TFLite 网络。
    /// </summary>
    /// <param name="bufferModel">.tflite 模型字节数据。</param>
    /// <param name="engine">DNN 引擎类型（默认 Auto）。</param>
    public static Net ReadNetFromTFLite(byte[] bufferModel, DnnEngineType engine = DnnEngineType.AUTO)
    {
        unsafe
        {
            fixed (byte* p = bufferModel)
            {
                var h = EzCvDll.DnnReadNetFromTfliteMem(p, bufferModel.Length, (int)engine);
                EzCvError.ThrowIfAny();
                return new Net(h);
            }
        }
    }

    /// <summary>
    /// 从 ONNX 文件读取张量。
    /// </summary>
    /// <param name="path">ONNX 文件路径。</param>
    /// <returns>读取的张量（Mat 对象）。</returns>
    public static Mat ReadTensorFromONNX(string path)
    {
        var h = EzCvDll.DnnReadTensorFromOnnx(path);
        EzCvError.ThrowIfAny();
        return new Mat(h, ownsHandle: true);
    }

    // =========================================================================
    // 实例方法
    // =========================================================================

    /// <summary>网络是否为空（无层）。</summary>
    public bool Empty()
    {
        var r = EzCvDll.DnnNetEmpty(Handle);
        EzCvError.ThrowIfAny();
        return r != 0;
    }

    /// <summary>设置网络输入 blob。</summary>
    /// <param name="blob">输入 blob（通常由 CvDnn.BlobFromImage 创建）。</param>
    /// <param name="name">输入名称（默认空串，即默认输入）。</param>
    public void SetInput(Mat blob, string name = "")
    {
        EzCvDll.DnnNetSetInput(Handle, blob.Handle, name);
        EzCvError.ThrowIfAny();
    }

    /// <summary>设置网络输入名称。</summary>
    public void SetInputsNames(string[] inputBlobNames)
    {
        EzCvDll.DnnNetSetInputsNames(Handle, inputBlobNames, inputBlobNames.Length);
        EzCvError.ThrowIfAny();
    }

    /// <summary>
    /// 前向传播（单输出）。
    /// </summary>
    /// <param name="outputName">输出层名称（空串表示整个网络的最终输出）。</param>
    /// <returns>输出 blob。</returns>
    public Mat Forward(string? outputName = null)
    {
        var h = EzCvDll.DnnNetForward(Handle, outputName);
        EzCvError.ThrowIfAny();
        return new Mat(h, ownsHandle: true);
    }

    /// <summary>
    /// 前向传播（多输出）。
    /// </summary>
    /// <param name="outputBlobs">预分配的输出 Mat 数组。</param>
    /// <param name="outputName">输出层名称（空串表示整个网络的最终输出）。</param>
    public void Forward(Mat[] outputBlobs, string? outputName = null)
    {
        var handles = new IntPtr[outputBlobs.Length];
        for (int i = 0; i < outputBlobs.Length; i++)
            handles[i] = outputBlobs[i].Handle;
        EzCvDll.DnnNetForwardMulti(Handle, handles, outputBlobs.Length, outputName);
        EzCvError.ThrowIfAny();
    }

    /// <summary>设置计算后端。</summary>
    public void SetPreferableBackend(Backend backendId)
    {
        EzCvDll.DnnNetSetPreferableBackend(Handle, (int)backendId);
        EzCvError.ThrowIfAny();
    }

    /// <summary>设置目标设备。</summary>
    public void SetPreferableTarget(Target targetId)
    {
        EzCvDll.DnnNetSetPreferableTarget(Handle, (int)targetId);
        EzCvError.ThrowIfAny();
    }

    /// <summary>获取层 ID，未找到返回 -1。</summary>
    public int GetLayerId(string layerName)
    {
        var id = EzCvDll.DnnNetGetLayerId(Handle, layerName);
        EzCvError.ThrowIfAny();
        return id;
    }

    /// <summary>获取所有层名称。</summary>
    public string[] GetLayerNames()
    {
        unsafe
        {
            char** names = null;
            int count = EzCvDll.DnnNetGetLayerNames(Handle, &names);
            EzCvError.ThrowIfAny();
            return ReadStringArray(names, count);
        }
    }

    /// <summary>获取未连接输出层的 ID 数组。</summary>
    public int[] GetUnconnectedOutLayers()
    {
        unsafe
        {
            int* ids = null;
            int count = EzCvDll.DnnNetGetUnconnectedOutLayers(Handle, &ids);
            EzCvError.ThrowIfAny();
            var result = new int[count];
            if (count > 0)
            {
                for (int i = 0; i < count; i++)
                    result[i] = ids[i];
                EzCvDll.FreeBuf((IntPtr)ids);
            }
            return result;
        }
    }

    /// <summary>获取未连接输出层的名称数组。</summary>
    public string[] GetUnconnectedOutLayersNames()
    {
        unsafe
        {
            char** names = null;
            int count = EzCvDll.DnnNetGetUnconnectedOutLayersNames(Handle, &names);
            EzCvError.ThrowIfAny();
            return ReadStringArray(names, count);
        }
    }

    /// <summary>
    /// 获取推理性能分析数据。
    /// </summary>
    /// <param name="timings">各层耗时（ticks）。</param>
    /// <returns>总推理 ticks。</returns>
    public long GetPerfProfile(out double[] timings)
    {
        unsafe
        {
            double* tPtr = null;
            int tCount = 0;
            long total = EzCvDll.DnnNetGetPerfProfile(Handle, &tPtr, &tCount);
            EzCvError.ThrowIfAny();
            timings = new double[tCount];
            if (tCount > 0)
            {
                for (int i = 0; i < tCount; i++)
                    timings[i] = tPtr[i];
                EzCvDll.FreeBuf((IntPtr)tPtr);
            }
            return total;
        }
    }

    /// <summary>
    /// 输出网络结构描述。
    /// </summary>
    public string Dump()
    {
        unsafe
        {
            var ptr = EzCvDll.DnnNetDump(Handle);
            EzCvError.ThrowIfAny();
            var result = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(ptr) ?? "";
            EzCvDll.FreeBuf(ptr);
            return result;
        }
    }

    // =========================================================================
    // 内部辅助
    // =========================================================================

    /// <summary>读取 native 返回的 char** 字符串数组，然后释放。</summary>
    private static unsafe string[] ReadStringArray(char** nativeArr, int count)
    {
        if (count == 0 || nativeArr == null) return [];
        var result = new string[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = System.Runtime.InteropServices.Marshal.PtrToStringUTF8((IntPtr)nativeArr[i]) ?? "";
        }
        EzCvDll.FreeStringArray((IntPtr)nativeArr, count);
        return result;
    }

    // =========================================================================
    // IDisposable
    // =========================================================================

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (Handle != IntPtr.Zero)
        {
            EzCvDll.DnnNetRelease(Handle);
            Handle = IntPtr.Zero;
        }
        GC.SuppressFinalize(this);
    }
}
