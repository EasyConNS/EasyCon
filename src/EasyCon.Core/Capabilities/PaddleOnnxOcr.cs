using EasyCon.Capture;
using EasyCon.Capture.Ocr;
using EasyCon.Capture.Ocr.Onnx;

namespace EasyCon.Core.Capabilities;

/// <summary>
/// PaddleOCR det+rec ONNX 后端 <see cref="IOcrService"/>（OpenCvSharp.Dnn 推理，D1 决策）。
/// 模型为外部资产不入库，命名沿用 <see cref="OnnxEngineFactory"/>：
/// 检测 PP-OCRv5_mobile_det.onnx；识别 &lt;lang&gt;_rec.onnx（缺省回落 PP-OCRv5_mobile_rec.onnx），
/// 字符集 &lt;lang&gt;_dict.txt（缺省回落 ppocr_keys_v5.txt）。
/// </summary>
public sealed class PaddleOnnxOcr : EngineCacheOcrService
{
    /// <param name="modelDirectory">模型/字符集目录（未入库，须现场部署）。</param>
    /// <param name="gpu">GPU 后端映射（Metal/CoreML 无 DNN 后端，回落 CPU）。</param>
    /// <param name="cache">外部注入的缓存（测试桩用）；缺省自建并预置 DefaultDataPath。</param>
    public PaddleOnnxOcr(string modelDirectory, GpuBackend gpu = GpuBackend.Cpu, OcrEngineCache? cache = null)
        : base(new OnnxEngineFactory(modelDirectory, backend: gpu), cache)
    {
        if (cache == null)
            Cache.DefaultDataPath = modelDirectory;
    }

    public override string Backend => "paddle-onnx";
}