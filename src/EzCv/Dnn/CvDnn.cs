using System.Runtime.InteropServices;
using EzCv.Interop;

namespace EzCv.Dnn;

/// <summary>
/// DNN 静态工具函数，API 兼容 OpenCvSharp.Dnn.CvDnn。
/// </summary>
public static class CvDnn
{
    /// <summary>
    /// 从 ONNX 文件读取网络模型。
    /// </summary>
    /// <param name="onnxFile">ONNX 文件路径。</param>
    /// <returns>加载的网络模型。</returns>
    public static Net ReadNetFromOnnx(string onnxFile)
    {
        return Net.ReadNetFromONNX(onnxFile);
    }

    /// <summary>
    /// 从 ONNX 字节数组读取网络模型。
    /// </summary>
    /// <param name="onnxFileData">ONNX 文件字节数据。</param>
    /// <returns>加载的网络模型。</returns>
    public static Net ReadNetFromOnnx(byte[] onnxFileData)
    {
        return Net.ReadNetFromONNX(onnxFileData);
    }

    /// <summary>
    /// 从 ONNX 内存区域读取网络模型。
    /// </summary>
    /// <param name="onnxFileData">ONNX 文件内存区域。</param>
    /// <returns>加载的网络模型。</returns>
    public static Net ReadNetFromOnnx(ReadOnlySpan<byte> onnxFileData, DnnEngineType engine = DnnEngineType.AUTO)
    {
        unsafe
        {
            fixed (byte* ptr = onnxFileData)
            {
                var handle = EzCvDll.DnnReadNetFromOnnxMem(ptr, onnxFileData.Length, (int)engine);
                EzCvError.ThrowIfAny();
                return new Net(handle);
            }
        }
    }

    /// <summary>
    /// 从 ONNX 流读取网络模型。
    /// </summary>
    /// <param name="onnxFileStream">ONNX 文件流。</param>
    /// <returns>加载的网络模型。</returns>
    public static Net ReadNetFromOnnx(Stream onnxFileStream)
    {
        if (onnxFileStream == null)
            throw new ArgumentNullException(nameof(onnxFileStream));

        using var ms = new MemoryStream();
        onnxFileStream.CopyTo(ms);
        return ReadNetFromOnnx(ms.ToArray());
    }

    /// <summary>
    /// 从模型文件读取网络模型（自动检测格式）。
    /// </summary>
    /// <param name="model">模型文件路径（.onnx / .pb / .tflite 等）。</param>
    /// <param name="config">配置文件路径（可选，如 .pbtxt）。</param>
    /// <param name="framework">框架名称（可选，用于明确指定格式）。</param>
    /// <param name="engine">DNN 引擎类型（默认 AUTO）。</param>
    /// <returns>加载的网络模型。</returns>
    public static Net ReadNet(string model, string config = "", string framework = "", DnnEngineType engine = DnnEngineType.AUTO)
    {
        return Net.ReadNet(model, config, framework, engine);
    }

    /// <summary>
    /// 从 TensorFlow 模型文件读取网络模型。
    /// </summary>
    /// <param name="model">.pb 模型文件路径。</param>
    /// <param name="config">.pbtxt 配置文件路径（可选）。</param>
    /// <param name="engine">DNN 引擎类型（默认 AUTO）。</param>
    /// <returns>加载的网络模型。</returns>
    public static Net ReadNetFromTensorflow(string model, string config = "", DnnEngineType engine = DnnEngineType.AUTO)
    {
        return Net.ReadNetFromTensorflow(model, config, engine);
    }

    /// <summary>
    /// 从 TensorFlow 字节数组读取网络模型。
    /// </summary>
    /// <param name="bufferModel">.pb 模型字节数据。</param>
    /// <param name="bufferConfig">.pbtxt 配置字节数据（可选）。</param>
    /// <param name="engine">DNN 引擎类型（默认 AUTO）。</param>
    /// <returns>加载的网络模型。</returns>
    public static Net ReadNetFromTensorflow(byte[] bufferModel, byte[]? bufferConfig = null, DnnEngineType engine = DnnEngineType.AUTO)
    {
        return Net.ReadNetFromTensorflow(bufferModel, bufferConfig, engine);
    }

    /// <summary>
    /// 从 TensorFlow 流读取网络模型。
    /// </summary>
    /// <param name="modelStream">.pb 模型流。</param>
    /// <param name="configStream">.pbtxt 配置流（可选）。</param>
    /// <param name="engine">DNN 引擎类型（默认 AUTO）。</param>
    /// <returns>加载的网络模型。</returns>
    public static Net ReadNetFromTensorflow(Stream modelStream, Stream? configStream = null, DnnEngineType engine = DnnEngineType.AUTO)
    {
        if (modelStream == null)
            throw new ArgumentNullException(nameof(modelStream));

        using var msModel = new MemoryStream();
        modelStream.CopyTo(msModel);
        var modelBytes = msModel.ToArray();

        byte[]? configBytes = null;
        if (configStream != null)
        {
            using var msConfig = new MemoryStream();
            configStream.CopyTo(msConfig);
            configBytes = msConfig.ToArray();
        }

        return ReadNetFromTensorflow(modelBytes, configBytes, engine);
    }

    /// <summary>
    /// 从 TFLite 模型文件读取网络模型。
    /// </summary>
    /// <param name="model">.tflite 模型文件路径。</param>
    /// <param name="engine">DNN 引擎类型（默认 AUTO）。</param>
    /// <returns>加载的网络模型。</returns>
    public static Net ReadNetFromTFLite(string model, DnnEngineType engine = DnnEngineType.AUTO)
    {
        return Net.ReadNetFromTFLite(model, engine);
    }

    /// <summary>
    /// 从 TFLite 字节数组读取网络模型。
    /// </summary>
    /// <param name="bufferModel">.tflite 模型字节数据。</param>
    /// <param name="engine">DNN 引擎类型（默认 AUTO）。</param>
    /// <returns>加载的网络模型。</returns>
    public static Net ReadNetFromTFLite(byte[] bufferModel, DnnEngineType engine = DnnEngineType.AUTO)
    {
        return Net.ReadNetFromTFLite(bufferModel, engine);
    }

    /// <summary>
    /// 从 TFLite 流读取网络模型。
    /// </summary>
    /// <param name="modelStream">.tflite 模型流。</param>
    /// <param name="engine">DNN 引擎类型（默认 AUTO）。</param>
    /// <returns>加载的网络模型。</returns>
    public static Net ReadNetFromTFLite(Stream modelStream, DnnEngineType engine = DnnEngineType.AUTO)
    {
        if (modelStream == null)
            throw new ArgumentNullException(nameof(modelStream));

        using var ms = new MemoryStream();
        modelStream.CopyTo(ms);
        return ReadNetFromTFLite(ms.ToArray(), engine);
    }

    /// <summary>
    /// 从 ONNX 文件读取张量。
    /// </summary>
    /// <param name="path">ONNX 文件路径。</param>
    /// <returns>读取的张量（Mat 对象）。</returns>
    public static Mat ReadTensorFromOnnx(string path)
    {
        return Net.ReadTensorFromONNX(path);
    }

    /// <summary>
    /// 从图像创建 4D blob（NCHW 格式）。
    /// </summary>
    /// <param name="image">输入图像（可以是任意通道数）。</param>
    /// <param name="scaleFactor">缩放因子（默认 1.0）。</param>
    /// <param name="size">输出 blob 的空间尺寸（宽 × 高）。</param>
    /// <param name="mean">各通道的均值（BGR 顺序）。</param>
    /// <param name="swapRB">是否交换 R 和 B 通道。</param>
    /// <param name="crop">是否在缩放后裁剪到精确尺寸。</param>
    /// <returns>4D blob Mat（NCHW 格式，32 位浮点）。</returns>
    public static Mat BlobFromImage(Mat image, double scaleFactor = 1.0,
        Size size = default, Scalar mean = default,
        bool swapRB = true, bool crop = true)
    {
        if (size.Width == 0 && size.Height == 0)
            size = new Size(image.Width, image.Height);

        var h = EzCvDll.DnnBlobFromImage(image.Handle, scaleFactor,
            size.Width, size.Height,
            mean.Val0, mean.Val1, mean.Val2,
            swapRB ? 1 : 0, crop ? 1 : 0);
        EzCvError.ThrowIfAny();
        return new Mat(h, ownsHandle: true);
    }

    /// <summary>
    /// 非极大值抑制（Non-Maximum Suppression）。
    /// </summary>
    /// <param name="bboxes">边界框数组（每个框为 [x, y, width, height]）。</param>
    /// <param name="scores">每个框的置信度分数。</param>
    /// <param name="scoreThreshold">置信度阈值（过滤低分框）。</param>
    /// <param name="nmsThreshold">NMS IoU 阈值。</param>
    /// <returns>被选中的框在 bboxes 数组中的索引。</returns>
    public static int[] NMSBoxes(Rect[] bboxes, float[] scores,
        float scoreThreshold, float nmsThreshold, float eta = 1.0f, int topK = 0)
    {
        if (bboxes.Length != scores.Length)
            throw new ArgumentException("bboxes 和 scores 长度必须相同");

        var count = bboxes.Length;
        if (count == 0) return [];

        // 将 Rect[] 转换为 int[] 格式 [x, y, w, h, x, y, w, h, ...]
        var bboxesFlat = new int[count * 4];
        for (int i = 0; i < count; i++)
        {
            bboxesFlat[i * 4 + 0] = bboxes[i].X;
            bboxesFlat[i * 4 + 1] = bboxes[i].Y;
            bboxesFlat[i * 4 + 2] = bboxes[i].Width;
            bboxesFlat[i * 4 + 3] = bboxes[i].Height;
        }

        // 预分配最大可能的输出
        var indices = new int[count];
        int selected = EzCvDll.DnnNmsBoxes(bboxesFlat, scores, count,
            scoreThreshold, nmsThreshold, indices, eta, topK);
        EzCvError.ThrowIfAny();

        // 截取实际选中的部分
        var result = new int[selected];
        Array.Copy(indices, result, selected);
        return result;
    }
}
