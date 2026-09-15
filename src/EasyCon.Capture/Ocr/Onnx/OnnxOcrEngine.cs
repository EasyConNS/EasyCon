using EasyCon.Capture.Ocr;
using EzCv;
using EzCv.Dnn;

namespace EasyCon.Capture.Ocr.Onnx;

/// <summary>
/// OpenCV DNN 文本检测器 — 基于 PaddleOCR 检测模型（如 PP-OCRv5_mobile_det.onnx）。
/// 实现 IOcrDetector，从图片中定位文字区域。
/// </summary>
public sealed class OnnxDetector : IOcrDetector
{
    private readonly Net _net;
    private readonly DetOptions _options;
    private bool _disposed;

    /// <summary>
    /// 从文件路径加载检测模型。
    /// </summary>
    /// <param name="modelPath">ONNX 模型文件路径（如 PP-OCRv5_mobile_det.onnx）。</param>
    /// <param name="options">检测配置（可选）。</param>
    /// <param name="backend">DNN 计算后端（默认 DEFAULT）。</param>
    /// <param name="target">DNN 目标设备（默认 CPU）。</param>
    /// <exception cref="FileNotFoundException">模型文件不存在。</exception>
    public OnnxDetector(string modelPath, DetOptions? options = null,
        Backend backend = Backend.DEFAULT, Target target = Target.CPU)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Detection model not found: {modelPath}");

        _options = options ?? new DetOptions();
        ValidateOptions(_options);
        _net = CvDnn.ReadNetFromOnnx(modelPath);
        _net.SetPreferableBackend(backend);
        _net.SetPreferableTarget(target);
    }

    /// <inheritdoc/>
    public OcrTextBox[] Detect(byte[] image)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(OnnxDetector));
        if (image == null || image.Length == 0) return [];

        using var src = image.ToMat();
        if (src.Empty()) return [];

        using var preprocessed = PreprocessDet(src, out float scaleX, out float scaleY);
        using var blob = CreateDetBlob(preprocessed);

        _net.SetInput(blob);
        using var output = _net.Forward();

        return PostprocessDet(output, _options.BoxThreshold, scaleX, scaleY, src.Width, src.Height);
    }

    /// <summary>
    /// 检测预处理：按配置调整通道顺序、限制最大边长并对齐输入尺寸。
    /// </summary>
    private Mat PreprocessDet(Mat src, out float scaleX, out float scaleY)
    {
        using var ordered = new Mat();
        if (_options.SwapRedBlue)
            Cv2.CvtColor(src, ordered, ColorConversionCodes.BGR2RGB);
        else
            src.ConvertTo(ordered, src.Type());

        // 缩放到 max_side_len
        float ratio = (float)_options.MaxSideLen / Math.Max(ordered.Width, ordered.Height);
        if (ratio < 1.0f)
        {
            int newW = Math.Max(1, (int)(ordered.Width * ratio));
            int newH = Math.Max(1, (int)(ordered.Height * ratio));
            Cv2.Resize(ordered, ordered, new Size(newW, newH), 0, 0, InterpolationFlags.Linear);
        }

        scaleX = (float)src.Width / ordered.Width;
        scaleY = (float)src.Height / ordered.Height;

        int multiple = _options.InputMultiple;
        int padW = (multiple - ordered.Width % multiple) % multiple;
        int padH = (multiple - ordered.Height % multiple) % multiple;
        double value = _options.PaddingValue;
        Cv2.CopyMakeBorder(ordered, ordered, 0, padH, 0, padW, BorderTypes.Constant,
            new Scalar(value, value, value));

        return ordered.Clone();
    }

    /// <summary>
    /// 创建检测模型输入 blob：BlobFromImage（mean 减法） + 手动除以 std。
    /// 归一化公式：(pixel/255 - mean) / std，NCHW 布局。
    /// </summary>
    private unsafe Mat CreateDetBlob(Mat image)
    {
        int w = image.Width, h = image.Height;

        var blob = CvDnn.BlobFromImage(image, _options.Scale,
            new Size(w, h),
            new Scalar(_options.Mean[0], _options.Mean[1], _options.Mean[2]),
            swapRB: false, crop: false);

        // 手动除以 std（BlobFromImage 不支持 std 归一化）
        float* pData = (float*)blob.Data;
        int planeSize = h * w;
        float* ch0 = pData;
        float* ch1 = pData + planeSize;
        float* ch2 = pData + 2 * planeSize;
        for (int i = 0; i < planeSize; i++)
        {
            ch0[i] /= (float)_options.StandardDeviation[0];
            ch1[i] /= (float)_options.StandardDeviation[1];
            ch2[i] /= (float)_options.StandardDeviation[2];
        }

        return blob;
    }

    /// <summary>
    /// 检测后处理：DB 算法 — 阈值过滤 → 找连通域 → 生成文本框。
    /// </summary>
    private OcrTextBox[] PostprocessDet(
        Mat output, float threshold,
        float scaleX, float scaleY, int origW, int origH)
    {
        if (output.Dims != 4 || output.Size(0) != 1 || output.Size(1) != 1)
            throw new InvalidDataException("ONNX OCR detector output must have shape [1,1,H,W].");
        if ((output.Type() & 7) != MatType.CV_32F)
            throw new InvalidDataException("ONNX OCR detector output must contain float32 scores.");

        int h = output.Size(2);
        int w = output.Size(3);

        // 构建概率二值图
        using var prob = new Mat(h, w, MatType.CV_8UC1);
        unsafe
        {
            float* pOutput = (float*)output.Data;
            byte* pData = (byte*)prob.Data;
            int step = (int)prob.Step();
            for (int y = 0; y < h; y++)
            {
                byte* row = pData + y * step;
                for (int x = 0; x < w; x++)
                {
                    row[x] = pOutput[y * w + x] > threshold ? (byte)255 : (byte)0;
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

    private static void ValidateOptions(DetOptions options)
    {
        if (options.MaxSideLen <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxSideLen must be positive.");
        if (options.InputMultiple <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "InputMultiple must be positive.");
        if (!float.IsFinite(options.BoxThreshold) || options.BoxThreshold is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(options), "BoxThreshold must be between 0 and 1.");
        if (!double.IsFinite(options.Scale))
            throw new ArgumentOutOfRangeException(nameof(options), "Scale must be finite.");
        if (options.Mean == null || options.Mean.Length != 3
            || options.Mean.Any(value => !double.IsFinite(value)))
            throw new ArgumentException("Mean must contain exactly three values.", nameof(options));
        if (options.StandardDeviation == null || options.StandardDeviation.Length != 3
            || options.StandardDeviation.Any(value => !double.IsFinite(value) || value <= 0))
            throw new ArgumentException("StandardDeviation must contain exactly three positive values.", nameof(options));
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
        _net.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// OpenCV DNN 文本识别器 — 基于 PaddleOCR 识别模型（如 PP-OCRv5_mobile_rec.onnx）。
/// 实现 IOcrRecognizer，从已裁剪的文字行图片中识别文本。
/// </summary>
public sealed class OnnxRecognizer : IOcrRecognizer
{
    private readonly Net _net;
    private readonly string[] _charset;
    private readonly RecOptions _options;
    private bool _disposed;

    /// <summary>
    /// 从文件路径加载识别模型和字符集。
    /// </summary>
    /// <param name="modelPath">ONNX 模型文件路径（如 PP-OCRv5_mobile_rec.onnx）。</param>
    /// <param name="charsetPath">字符集文件路径（每行一个类别文本；CTC 空白类别不写入词典）。</param>
    /// <param name="options">识别配置（可选）。</param>
    /// <param name="backend">DNN 计算后端（默认 DEFAULT）。</param>
    /// <param name="target">DNN 目标设备（默认 CPU）。</param>
    /// <exception cref="FileNotFoundException">模型或字符集文件不存在。</exception>
    public OnnxRecognizer(string modelPath, string charsetPath, RecOptions? options = null,
        Backend backend = Backend.DEFAULT, Target target = Target.CPU)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Recognition model not found: {modelPath}");
        if (!File.Exists(charsetPath))
            throw new FileNotFoundException($"Charset file not found: {charsetPath}");

        _options = options ?? new RecOptions();
        ValidateOptions(_options);
        _net = CvDnn.ReadNetFromOnnx(modelPath);
        _net.SetPreferableBackend(backend);
        _net.SetPreferableTarget(target);
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
        using var blob = CreateRecBlob(preprocessed);

        _net.SetInput(blob);
        using var output = _net.Forward();

        return CtcDecode(output);
    }

    /// <summary>
    /// 识别预处理：按配置调整通道顺序、高度和宽度对齐。
    /// </summary>
    private Mat PreprocessRec(Mat src)
    {
        using var ordered = new Mat();
        if (_options.SwapRedBlue)
            Cv2.CvtColor(src, ordered, ColorConversionCodes.BGR2RGB);
        else
            src.ConvertTo(ordered, src.Type());

        int newW = (int)Math.Round((double)ordered.Width * _options.ImageHeight / ordered.Height);
        newW = Math.Clamp(newW, _options.MinImageWidth, _options.MaxImageWidth);
        int inputW = AlignWidth(newW, _options.WidthMultiple, _options.MaxImageWidth);
        newW = Math.Min(newW, inputW);

        using var resized = new Mat();
        Cv2.Resize(ordered, resized, new Size(newW, _options.ImageHeight), 0, 0, InterpolationFlags.Linear);
        if (inputW == newW) return resized.Clone();

        var padded = new Mat();
        double value = _options.PaddingValue;
        Cv2.CopyMakeBorder(resized, padded, 0, 0, 0, inputW - newW,
            BorderTypes.Constant, new Scalar(value, value, value));
        return padded;
    }

    /// <summary>
    /// 创建识别模型输入 blob。OpenCV 的归一化公式为
    /// <c>(pixel - mean) * scale</c>。
    /// </summary>
    private Mat CreateRecBlob(Mat image)
    {
        return CvDnn.BlobFromImage(image,
            _options.Scale,
            new Size(image.Width, image.Height),
            new Scalar(_options.Mean[0], _options.Mean[1], _options.Mean[2]),
            swapRB: false, crop: false);
    }

    /// <summary>
    /// CTC 贪心解码：去重 + 去空白符 → 查字符集。
    /// 输出 Mat 支持 [T, C]、[1, T, C] 或尾部为 1 的 [1, T, C, ...]。
    /// </summary>
    private OcrRecognizeResult CtcDecode(Mat output)
    {
        int T, numClasses;

        if (output.Dims == 2)
        {
            T = output.Size(0);
            numClasses = output.Size(1);
        }
        else if (output.Dims >= 3 && output.Size(0) == 1)
        {
            T = output.Size(1);
            numClasses = output.Size(2);
            for (int i = 3; i < output.Dims; i++)
                if (output.Size(i) != 1)
                    throw new InvalidDataException("ONNX OCR output has unsupported trailing dimensions.");
        }
        else
        {
            throw new InvalidDataException("ONNX OCR output must have shape [T,C] or [1,T,C].");
        }

        if ((output.Type() & 7) != MatType.CV_32F)
            throw new InvalidDataException("ONNX OCR output must contain float32 scores.");

        unsafe
        {
            var scores = new ReadOnlySpan<float>((void*)output.Data, checked(T * numClasses));
            return PaddleCtcDecoder.Decode(scores, T, numClasses, _charset, _options);
        }
    }

    internal static int AlignWidth(int width, int multiple, int maximum)
    {
        int aligned = (int)Math.Min((long)int.MaxValue,
            ((long)width + multiple - 1) / multiple * multiple);
        if (aligned <= maximum) return aligned;
        int bounded = maximum / multiple * multiple;
        return bounded > 0 ? bounded : maximum;
    }

    private static void ValidateOptions(RecOptions options)
    {
        if (options.ImageHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "ImageHeight must be positive.");
        if (options.MinImageWidth <= 0 || options.MaxImageWidth < options.MinImageWidth)
            throw new ArgumentOutOfRangeException(nameof(options), "Invalid recognition width range.");
        if (options.WidthMultiple <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "WidthMultiple must be positive.");
        if (options.Mean == null || options.Mean.Length != 3
            || options.Mean.Any(value => !double.IsFinite(value)))
            throw new ArgumentException("Mean must contain exactly three values.", nameof(options));
        if (!double.IsFinite(options.Scale))
            throw new ArgumentOutOfRangeException(nameof(options), "Scale must be finite.");
        if (!float.IsFinite(options.MinScore) || options.MinScore is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(options), "MinScore must be between 0 and 1.");
        if (options.BlankIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "BlankIndex cannot be negative.");
    }

    /// <summary>加载字符集文件。CTC 空白类别不写入词典。</summary>
    private static string[] LoadCharset(string path)
    {
        return File.ReadAllLines(path);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _net.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// OpenCV DNN 端到端 OCR 引擎 — 组合 OnnxDetector + OnnxRecognizer 实现检测+识别一体化。
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
/// ONNX 引擎工厂 — 创建 OpenCV DNN 识别器 / 端到端引擎。
/// 实现 IOcrEngineFactory，供 OcrEngineCache 插件式使用。
/// </summary>
public sealed class OnnxEngineFactory : IOcrEngineFactory
{
    private readonly string _modelDirectory;
    private readonly string _detModelName;
    private readonly Backend _backend;
    private readonly Target _target;

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
        (_backend, _target) = OnnxProviderMapper.MapBackend(backend);
    }

    /// <inheritdoc/>
    public IOcrRecognizer CreateRecognizer(string lang, string dataPath, string engineMode, string psmode)
    {
        ResolvedModels models = ResolveModels(lang, dataPath, psmode);
        return new OnnxRecognizer(models.RecognitionModel, models.CharacterDictionary,
            models.Config.Recognition, _backend, _target);
    }

    /// <inheritdoc/>
    public IOcrEngine? CreateEngine(string lang, string dataPath, string engineMode, string psmode)
    {
        ResolvedModels models = ResolveModels(lang, dataPath, psmode);
        return new OnnxOcrEngine(
            new OnnxDetector(models.DetectionModel, models.Config.Detection, _backend, _target),
            new OnnxRecognizer(models.RecognitionModel, models.CharacterDictionary,
                models.Config.Recognition, _backend, _target));
    }

    private ResolvedModels ResolveModels(string lang, string dataPath, string psmode)
    {
        string modelDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(dataPath) ? _modelDirectory : dataPath);
        string? configPath = ResolveConfigPath(modelDirectory, lang, psmode);
        OnnxOcrModelConfig config = configPath == null ? new() : OnnxOcrModelConfig.Load(configPath);
        string resourceDirectory = configPath == null ? modelDirectory : Path.GetDirectoryName(configPath)!;

        string recognitionModel = ResolveResource(config.RecognitionModel, resourceDirectory,
            Path.Combine(modelDirectory, $"{lang}_rec.onnx"),
            Path.Combine(modelDirectory, "PP-OCRv5_mobile_rec.onnx"));
        string dictionary = ResolveResource(config.CharacterDictionary, resourceDirectory,
            Path.Combine(modelDirectory, $"{lang}_dict.txt"),
            Path.Combine(modelDirectory, "ppocr_keys_v5.txt"));
        string detectionModel = ResolveResource(config.DetectionModel, resourceDirectory,
            Path.Combine(modelDirectory, _detModelName));

        return new(config, recognitionModel, dictionary, detectionModel);
    }

    private static string? ResolveConfigPath(string modelDirectory, string lang, string psmode)
    {
        if (!string.IsNullOrWhiteSpace(psmode)
            && psmode.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            string explicitPath = Path.GetFullPath(Path.IsPathRooted(psmode)
                ? psmode : Path.Combine(modelDirectory, psmode));
            if (!File.Exists(explicitPath))
                throw new FileNotFoundException($"ONNX OCR config not found: {explicitPath}");
            return explicitPath;
        }

        string sidecar = Path.Combine(modelDirectory, $"{lang}_ocr.json");
        return File.Exists(sidecar) ? sidecar : null;
    }

    private static string ResolveResource(string configuredPath, string resourceDirectory,
        params string[] fallbacks)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
            return Path.GetFullPath(Path.IsPathRooted(configuredPath)
                ? configuredPath : Path.Combine(resourceDirectory, configuredPath));
        return fallbacks.FirstOrDefault(File.Exists) ?? fallbacks[0];
    }

    private sealed record ResolvedModels(OnnxOcrModelConfig Config, string RecognitionModel,
        string CharacterDictionary, string DetectionModel);
}

internal static class PaddleCtcDecoder
{
    public static OcrRecognizeResult Decode(ReadOnlySpan<float> scores, int timeSteps, int classCount,
        IReadOnlyList<string> dictionary, RecOptions options)
    {
        if (timeSteps < 0 || classCount <= 0 || scores.Length != checked(timeSteps * classCount))
            throw new ArgumentException("CTC score shape does not match the supplied data.", nameof(scores));
        if (options.BlankIndex >= classCount)
            throw new InvalidDataException("CTC blank index is outside the model output.");

        int expectedClasses = dictionary.Count + 1;
        int maximumClasses = expectedClasses + (options.AppendSpaceClass ? 1 : 0);
        if (classCount < expectedClasses || classCount > maximumClasses)
            throw new InvalidDataException("ONNX OCR model and character dictionary shapes disagree.");

        var text = new System.Text.StringBuilder();
        float totalConfidence = 0;
        int emitted = 0;
        int previous = options.BlankIndex;

        for (int t = 0; t < timeSteps; t++)
        {
            ReadOnlySpan<float> row = scores.Slice(t * classCount, classCount);
            int best = 0;
            for (int c = 1; c < row.Length; c++)
                if (row[c] > row[best]) best = c;

            if (best != options.BlankIndex && best != previous)
            {
                int dictionaryIndex = best < options.BlankIndex ? best : best - 1;
                if (dictionaryIndex < dictionary.Count)
                    text.Append(dictionary[dictionaryIndex]);
                else
                    text.Append(' ');
                totalConfidence += row[best];
                emitted++;
            }
            previous = best;
        }

        float confidence = emitted == 0 ? 0 : totalConfidence / emitted;
        return confidence < options.MinScore
            ? new OcrRecognizeResult(string.Empty, confidence)
            : new OcrRecognizeResult(text.ToString(), confidence);
    }
}