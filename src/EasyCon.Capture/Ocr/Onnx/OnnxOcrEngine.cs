using EasyCon.Capture.Ocr;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using EzCv;

namespace EasyCon.Capture.Ocr.Onnx;

/// <summary>
/// ONNX Runtime 文本检测器 — 基于 PaddleOCR 检测模型（如 PP-OCRv5_mobile_det.onnx）。
/// 实现 IOcrDetector，从图片中定位文字区域。
/// </summary>
public sealed class OnnxDetector : IOcrDetector
{
    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly string _outputName;
    private readonly DetOptions _options;
    private bool _disposed;

    // PaddleOCR 检测模型预处理参数
    private static readonly float[] DetMean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] DetStd = [0.229f, 0.224f, 0.225f];

    /// <summary>
    /// 从文件路径加载检测模型。
    /// </summary>
    /// <param name="modelPath">ONNX 模型文件路径（如 PP-OCRv5_mobile_det.onnx）。</param>
    /// <param name="options">检测配置（可选）。</param>
    /// <param name="sessionOptions">ONNX Runtime 会话选项（可选，用于配置 GPU 等）。</param>
    /// <exception cref="FileNotFoundException">模型文件不存在。</exception>
    /// <exception cref="OnnxException">模型加载失败。</exception>
    public OnnxDetector(string modelPath, DetOptions? options = null, SessionOptions? sessionOptions = null)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Detection model not found: {modelPath}");

        _options = options ?? new DetOptions();
        _session = sessionOptions != null
            ? new InferenceSession(modelPath, sessionOptions)
            : new InferenceSession(modelPath);
        _inputName = _session.InputNames[0];
        _outputName = _session.OutputNames[0];
    }

    /// <inheritdoc/>
    public OcrTextBox[] Detect(byte[] image)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(OnnxDetector));
        if (image == null || image.Length == 0) return [];

        using var src = image.ToMat();
        if (src.Empty()) return [];

        using var preprocessed = PreprocessDet(src, out float scaleX, out float scaleY);
        var tensor = MatToTensor(preprocessed, DetMean, DetStd);

        using var results = _session.Run(
            [NamedOnnxValue.CreateFromTensor(_inputName, tensor)],
            [_outputName]);

        var output = results[0].AsTensor<float>();
        return PostprocessDet(output, _options.BoxThreshold, scaleX, scaleY, src.Width, src.Height);
    }

    /// <summary>
    /// 检测预处理：BGR→RGB、缩放（限制最大边长）、填充到 32 的倍数。
    /// </summary>
    private Mat PreprocessDet(Mat src, out float scaleX, out float scaleY)
    {
        using var rgb = new Mat();
        Cv2.CvtColor(src, rgb, ColorConversionCodes.BGR2RGB);

        // 缩放到 max_side_len
        float ratio = (float)_options.MaxSideLen / Math.Max(rgb.Width, rgb.Height);
        if (ratio < 1.0f)
        {
            int newW = (int)(rgb.Width * ratio);
            int newH = (int)(rgb.Height * ratio);
            Cv2.Resize(rgb, rgb, new Size(newW, newH), 0, 0, InterpolationFlags.Linear);
        }

        scaleX = (float)src.Width / rgb.Width;
        scaleY = (float)src.Height / rgb.Height;

        // 填充到 32 的倍数
        int padW = ((rgb.Width + 31) / 32) * 32 - rgb.Width;
        int padH = ((rgb.Height + 31) / 32) * 32 - rgb.Height;
        Cv2.CopyMakeBorder(rgb, rgb, 0, padH, 0, padW, BorderTypes.Constant, Scalar.Black);

        return rgb.Clone();
    }

    /// <summary>
    /// OpenCV Mat → ONNX Tensor（float32, CHW 布局，归一化）。
    /// </summary>
    private static DenseTensor<float> MatToTensor(Mat mat, float[] mean, float[] std)
    {
        int h = mat.Height, w = mat.Width;
        var tensor = new DenseTensor<float>([1, 3, h, w]);

        unsafe
        {
            byte* pData = (byte*)mat.Data;
            int step = (int)mat.Step();
            for (int c = 0; c < 3; c++)
            {
                for (int y = 0; y < h; y++)
                {
                    byte* row = pData + y * step;
                    for (int x = 0; x < w; x++)
                    {
                        float val = row[x * 3 + c] / 255.0f;
                        val = (val - mean[c]) / std[c];
                        tensor[0, c, y, x] = val;
                    }
                }
            }
        }

        return tensor;
    }

    /// <summary>
    /// 检测后处理：DB 算法 — 阈值过滤 → 找连通域 → 生成文本框。
    /// </summary>
    private OcrTextBox[] PostprocessDet(
        Tensor<float> output, float threshold,
        float scaleX, float scaleY, int origW, int origH)
    {
        int h = output.Dimensions[2];
        int w = output.Dimensions[3];

        // 构建概率二值图
        using var prob = new Mat(h, w, MatType.CV_8UC1);
        unsafe
        {
            byte* pData = (byte*)prob.Data;
            int step = (int)prob.Step();
            for (int y = 0; y < h; y++)
            {
                byte* row = pData + y * step;
                for (int x = 0; x < w; x++)
                {
                    row[x] = output[0, 0, y, x] > threshold ? (byte)255 : (byte)0;
                }
            }
        }

        // 找连通域
        Cv2.FindContours(prob, out var contours, out _, RetrievalModes.List, ContourApproximationModes.ApproxSimple);

        var boxes = new List<OcrTextBox>();
        foreach (var contour in contours)
        {
            var rect = Cv2.BoundingRect(contour);
            if (rect.Width < 3 || rect.Height < 3) continue; // 过滤过小区域

            int x = (int)(rect.X * scaleX);
            int y = (int)(rect.Y * scaleY);
            int bw = (int)(rect.Width * scaleX);
            int bh = (int)(rect.Height * scaleY);

            // 裁剪到原图范围
            x = Math.Clamp(x, 0, origW);
            y = Math.Clamp(y, 0, origH);
            bw = Math.Clamp(bw, 0, origW - x);
            bh = Math.Clamp(bh, 0, origH - y);

            boxes.Add(new OcrTextBox(x, y, bw, bh));
        }

        if (_options.MergeBoxes)
            boxes = MergeNearbyBoxes(boxes);

        return [.. boxes];
    }

    /// <summary>合并垂直方向重叠的相邻文本框。</summary>
    private static List<OcrTextBox> MergeNearbyBoxes(List<OcrTextBox> boxes)
    {
        if (boxes.Count <= 1) return boxes;

        // 按 Y 坐标排序
        boxes.Sort((a, b) => a.Y.CompareTo(b.Y));

        var merged = new List<OcrTextBox>();
        var current = boxes[0];

        for (int i = 1; i < boxes.Count; i++)
        {
            var next = boxes[i];
            // 垂直方向重叠或接近（阈值：平均高度的 0.3 倍）
            int avgH = (current.Height + next.Height) / 2;
            if (Math.Abs(current.Y - next.Y) < avgH * 0.3f)
            {
                int newX = Math.Min(current.X, next.X);
                int newY = Math.Min(current.Y, next.Y);
                int newR = Math.Max(current.X + current.Width, next.X + next.Width);
                int newB = Math.Max(current.Y + current.Height, next.Y + next.Height);
                current = new OcrTextBox(newX, newY, newR - newX, newB - newY);
            }
            else
            {
                merged.Add(current);
                current = next;
            }
        }
        merged.Add(current);
        return merged;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// ONNX Runtime 文本识别器 — 基于 PaddleOCR 识别模型（如 PP-OCRv5_mobile_rec.onnx）。
/// 实现 IOcrRecognizer，从已裁剪的文字行图片中识别文本。
/// </summary>
public sealed class OnnxRecognizer : IOcrRecognizer
{
    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly string _outputName;
    private readonly string[] _charset; // index → character
    private readonly RecOptions _options;
    private bool _disposed;

    private const int RecImageHeight = 32;

    /// <summary>
    /// 从文件路径加载识别模型和字符集。
    /// </summary>
    /// <param name="modelPath">ONNX 模型文件路径（如 PP-OCRv5_mobile_rec.onnx）。</param>
    /// <param name="charsetPath">字符集文件路径（每行一个字符，首行为空白符）。</param>
    /// <param name="options">识别配置（可选）。</param>
    /// <param name="sessionOptions">ONNX Runtime 会话选项（可选，用于配置 GPU 等）。</param>
    /// <exception cref="FileNotFoundException">模型或字符集文件不存在。</exception>
    public OnnxRecognizer(string modelPath, string charsetPath, RecOptions? options = null, SessionOptions? sessionOptions = null)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Recognition model not found: {modelPath}");
        if (!File.Exists(charsetPath))
            throw new FileNotFoundException($"Charset file not found: {charsetPath}");

        _options = options ?? new RecOptions();
        _session = sessionOptions != null
            ? new InferenceSession(modelPath, sessionOptions)
            : new InferenceSession(modelPath);
        _inputName = _session.InputNames[0];
        _outputName = _session.OutputNames[0];
        _charset = LoadCharset(charsetPath);
    }

    /// <inheritdoc/>
    public OcrRecognizeResult Recognize(byte[] image)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(OnnxRecognizer));
        if (image == null || image.Length == 0)
            return new OcrRecognizeResult(string.Empty, 0f);

        using var src = image.ToMat();
        if (src.Empty()) return new OcrRecognizeResult(string.Empty, 0f);

        using var preprocessed = PreprocessRec(src);
        var tensor = MatToRecTensor(preprocessed);

        using var results = _session.Run(
            [NamedOnnxValue.CreateFromTensor(_inputName, tensor)],
            [_outputName]);

        return CtcDecode(results[0].AsTensor<float>());
    }

    /// <summary>
    /// 识别预处理：BGR→RGB、高度缩放到 32（保持宽高比）、宽度填充到 8 的倍数。
    /// </summary>
    private static Mat PreprocessRec(Mat src)
    {
        using var rgb = new Mat();
        Cv2.CvtColor(src, rgb, ColorConversionCodes.BGR2RGB);

        float ratio = (float)RecImageHeight / rgb.Height;
        int newW = (int)(rgb.Width * ratio);
        Cv2.Resize(rgb, rgb, new Size(newW, RecImageHeight), 0, 0, InterpolationFlags.Linear);

        // 宽度填充到 8 的倍数（最小 8）
        int padW = ((newW + 7) / 8) * 8 - newW;
        if (padW > 0)
            Cv2.CopyMakeBorder(rgb, rgb, 0, 0, 0, padW, BorderTypes.Constant, Scalar.Black);

        return rgb.Clone();
    }

    /// <summary>
    /// 识别模型 Tensor 构建：归一化到 [-1, 1] 范围（(pixel/255 - 0.5) / 0.5）。
    /// </summary>
    private static DenseTensor<float> MatToRecTensor(Mat mat)
    {
        int h = mat.Height, w = mat.Width;
        var tensor = new DenseTensor<float>([1, 3, RecImageHeight, w]);

        unsafe
        {
            byte* pData = (byte*)mat.Data;
            int step = (int)mat.Step();
            for (int c = 0; c < 3; c++)
            {
                for (int y = 0; y < h; y++)
                {
                    byte* row = pData + y * step;
                    for (int x = 0; x < w; x++)
                    {
                        // (pixel/255 - 0.5) / 0.5 = pixel/127.5 - 1
                        tensor[0, c, y, x] = row[x * 3 + c] / 127.5f - 1.0f;
                    }
                }
            }
        }

        return tensor;
    }

    /// <summary>
    /// CTC 贪心解码：去重 + 去空白符 → 查字符集。
    /// </summary>
    private OcrRecognizeResult CtcDecode(Tensor<float> output)
    {
        int T = output.Dimensions[1];        // 时间步
        int numClasses = output.Dimensions[2]; // 类别数

        var indices = new List<int>();
        float totalConf = 0;
        int validCount = 0;

        int prevIdx = -1;
        for (int t = 0; t < T; t++)
        {
            // 找当前时间步最高概率的字符索引
            float maxProb = float.MinValue;
            int maxIdx = 0;
            for (int c = 0; c < numClasses; c++)
            {
                float p = output[0, t, c];
                if (p > maxProb) { maxProb = p; maxIdx = c; }
            }

            // 跳过空白符和重复
            if (maxIdx == 0) { prevIdx = -1; continue; }
            if (maxIdx == prevIdx) continue;

            prevIdx = maxIdx;
            indices.Add(maxIdx);
            totalConf += maxProb;
            validCount++;
        }

        if (indices.Count == 0)
            return new OcrRecognizeResult(string.Empty, 0f);

        // 查字符集
        var chars = new char[indices.Count];
        for (int i = 0; i < indices.Count; i++)
        {
            int idx = indices[i];
            chars[i] = idx < _charset.Length ? _charset[idx][0] : '?';
        }

        float confidence = validCount > 0 ? totalConf / validCount : 0f;
        if (confidence < _options.MinScore)
            return new OcrRecognizeResult(string.Empty, confidence);

        return new OcrRecognizeResult(new string(chars), confidence);
    }

    /// <summary>加载字符集文件：每行一个字符，首行为空白符。</summary>
    private static string[] LoadCharset(string path)
    {
        var lines = File.ReadAllLines(path);
        var charset = new string[lines.Length + 1];
        charset[0] = " "; // 空白符占位
        for (int i = 0; i < lines.Length; i++)
            charset[i + 1] = lines[i].TrimEnd('\r', '\n');
        return charset;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// ONNX Runtime 端到端 OCR 引擎 — 组合 OnnxDetector + OnnxRecognizer 实现检测+识别一体化。
/// 对应 ocr-rs 的 OcrEngine 层。
/// </summary>
public sealed class OnnxOcrEngine : IOcrEngine
{
    private readonly OnnxDetector _detector;
    private readonly OnnxRecognizer _recognizer;
    private bool _disposed;

    /// <summary>
    /// 从已有检测器和识别器创建端到端引擎。
    /// </summary>
    public OnnxOcrEngine(OnnxDetector detector, OnnxRecognizer recognizer)
    {
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        _recognizer = recognizer ?? throw new ArgumentNullException(nameof(recognizer));
    }

    /// <inheritdoc/>
    public OcrResult[] Recognize(byte[] image)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(OnnxOcrEngine));
        if (image == null || image.Length == 0) return [];

        using var src = image.ToMat();
        if (src.Empty()) return [];

        // 1. 检测文字区域
        var boxes = _detector.Detect(image);
        if (boxes.Length == 0) return [];

        // 2. 对每个区域裁剪并识别
        var results = new OcrResult[boxes.Length];
        for (int i = 0; i < boxes.Length; i++)
        {
            var box = boxes[i];
            var x = Math.Clamp(box.X, 0, src.Width);
            var y = Math.Clamp(box.Y, 0, src.Height);
            var w = Math.Clamp(box.Width, 0, src.Width - x);
            var h = Math.Clamp(box.Height, 0, src.Height - y);

            if (w == 0 || h == 0)
            {
                results[i] = new OcrResult(string.Empty, 0f, box);
                continue;
            }

            using var roi = new Mat(src, new Rect(x, y, w, h));
            var cropBytes = roi.ToBytes(".png");
            var recResult = _recognizer.Recognize(cropBytes);
            results[i] = new OcrResult(recResult.Text, recResult.Confidence, box);
        }

        return results;
    }

    /// <inheritdoc/>
    public OcrTextBox[] Detect(byte[] image) => _detector.Detect(image);

    /// <inheritdoc/>
    public IOcrDetector? Detector => _detector;

    /// <inheritdoc/>
    public IOcrRecognizer? Recognizer => _recognizer;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _detector.Dispose();
        _recognizer.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// ONNX 引擎工厂 — 创建 ONNX Runtime 识别器 / 端到端引擎。
/// 实现 IOcrEngineFactory，供 OcrEngineCache 插件式使用。
/// </summary>
public sealed class OnnxEngineFactory : IOcrEngineFactory
{
    private readonly string _modelDirectory;
    private readonly string _detModelName;
    private readonly SessionOptions _sessionOptions;

    /// <summary>
    /// 创建 ONNX 引擎工厂。
    /// </summary>
    /// <param name="modelDirectory">模型文件目录。</param>
    /// <param name="detModelName">检测模型文件名（默认 "PP-OCRv5_mobile_det.onnx"）。</param>
    /// <param name="backend">GPU 后端类型（默认 CPU）。</param>
    public OnnxEngineFactory(
        string modelDirectory,
        string detModelName = "PP-OCRv5_mobile_det.onnx",
        GpuBackend backend = GpuBackend.Cpu)
    {
        _modelDirectory = modelDirectory ?? throw new ArgumentNullException(nameof(modelDirectory));
        _detModelName = detModelName;
        _sessionOptions = OnnxProviderMapper.CreateSessionOptions(backend);
    }

    /// <inheritdoc/>
    public IOcrRecognizer CreateRecognizer(string lang, string dataPath, string engineMode, string psmode)
    {
        var modelDir = string.IsNullOrEmpty(dataPath) ? _modelDirectory : dataPath;
        var recModel = Path.Combine(modelDir, $"{lang}_rec.onnx");
        var charset = Path.Combine(modelDir, $"{lang}_dict.txt");

        if (!File.Exists(recModel))
        {
            // 回退：尝试不带语言前缀的通用模型名
            recModel = Path.Combine(modelDir, "PP-OCRv5_mobile_rec.onnx");
            charset = Path.Combine(modelDir, "ppocr_keys_v5.txt");
        }

        return new OnnxRecognizer(recModel, charset, sessionOptions: _sessionOptions);
    }

    /// <inheritdoc/>
    public IOcrEngine? CreateEngine(string lang, string dataPath, string engineMode, string psmode)
    {
        var modelDir = string.IsNullOrEmpty(dataPath) ? _modelDirectory : dataPath;
        var detPath = Path.Combine(modelDir, _detModelName);
        var recPath = Path.Combine(modelDir, $"{lang}_rec.onnx");
        var charsetPath = Path.Combine(modelDir, $"{lang}_dict.txt");

        if (!File.Exists(recPath))
        {
            recPath = Path.Combine(modelDir, "PP-OCRv5_mobile_rec.onnx");
            charsetPath = Path.Combine(modelDir, "ppocr_keys_v5.txt");
        }

        return new OnnxOcrEngine(
            new OnnxDetector(detPath, sessionOptions: _sessionOptions),
            new OnnxRecognizer(recPath, charsetPath, sessionOptions: _sessionOptions));
    }
}

/// <summary>
/// ONNX Runtime 异常。
/// </summary>
public sealed class OnnxException : Exception
{
    public OnnxException(string message) : base(message) { }
    public OnnxException(string message, Exception inner) : base(message, inner) { }
}