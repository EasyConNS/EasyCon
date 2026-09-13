// OpenCvDnnDemo — 用 EzCv（OpenCV 5 DNN）复刻 dnnpy/inference_onnx.py 的 YOLO11 ONNX 推理。
//
// 流程对照（行号对应 /Users/chaos/Desktop/dnnpy/inference_onnx.py）：
//   Main            ← 脚本底部 (L350-362)
//   Preprocess      ← preprocess (L86-125) : BGR→RGB + Letterbox + BlobFromImage(scale=1/255)
//   Letterbox       ← letterbox (L127-160) : 等比缩放 + (114,114,114) 灰边居中填充
//   Postprocess     ← postprocess_yolo11x (L227-295) : [1,84,8400] 转置/置信度过滤/坐标还原/NMS
//   DrawDetections  ← draw_detections (L298-334) : 画框 + 标签

using EzCv;
using EzCv.Dnn;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OpenCvDnnDemo;

internal static class Program
{
    // 模型输入尺寸（yolo11m.onnx 输入 [1,3,640,640]）
    private const int InputSize = 640;

    // 置信度阈值 & NMS IoU 阈值（与 Python 脚本一致）
    private const float ConfidenceThres = 0.2f;
    private const float IouThres = 0.5f;

    // 每类颜色板（固定 20 色，按 class_id 循环取色，保证多次运行结果稳定可比对）
    private static readonly (double R, double G, double B)[] Palette =
    [
        (255,  56,  56), ( 56, 255,  56), ( 56,  56, 255), (255, 255,  56),
        (255,  56, 255), ( 56, 255, 255), (255, 128,   0), (128,   0, 255),
        (  0, 255, 128), (255,   0, 128), (  0, 128, 255), (200, 200,   0),
        (200,   0, 200), (  0, 200, 200), (255, 200,   0), (200,   0, 255),
        (  0, 255, 200), (180,  90,  90), ( 90, 180,  90), ( 90,  90, 180),
    ];

    // COCO 80 类名称（与 Python CLASS_NAMES 一致，按 id 索引）
    private static readonly string[] ClassNames =
    [
        "person", "bicycle", "car", "motorcycle", "airplane", "bus", "train", "truck",
        "boat", "traffic light", "fire hydrant", "stop sign", "parking meter", "bench",
        "bird", "cat", "dog", "horse", "sheep", "cow", "elephant", "bear", "zebra",
        "giraffe", "backpack", "umbrella", "handbag", "tie", "suitcase", "frisbee",
        "skis", "snowboard", "sports ball", "kite", "baseball bat", "baseball glove",
        "skateboard", "surfboard", "tennis racket", "bottle", "wine glass", "cup",
        "fork", "knife", "spoon", "bowl", "banana", "apple", "sandwich", "orange",
        "broccoli", "carrot", "hot dog", "pizza", "donut", "cake", "chair", "couch",
        "potted plant", "bed", "dining table", "toilet", "tv", "laptop", "mouse",
        "remote", "keyboard", "cell phone", "microwave", "oven", "toaster", "sink",
        "refrigerator", "book", "clock", "vase", "scissors", "teddy bear",
        "hair drier", "toothbrush",
    ];

    private static void Main(string[] args)
    {
        // 资源路径：默认输出目录，允许命令行覆盖
        string baseDir = AppContext.BaseDirectory;
        string onnxPath = args.Length > 0 ? args[0] : Path.Combine(baseDir, "assets", "yolo11m.onnx");
        string imgPath = args.Length > 1 ? args[1] : Path.Combine(baseDir, "assets", "test.png");
        // null = 不过滤类别（检测全部 80 类）；0 = 只保留 person，与 Python 默认 class_id=0 一致
        int? filterClassId = args.Length > 2 ? int.Parse(args[2]) : 0;
        string outPath = Path.Combine(baseDir, "det_result.jpg");

        Console.WriteLine("YOLO11 🚀 目标检测 (EzCv / OpenCV DNN)");
        Console.WriteLine($"模型名称：{onnxPath}");
        Console.WriteLine($"输入尺寸：{InputSize}x{InputSize}");

        // 1) 加载 ONNX 模型
        using var net = CvDnn.ReadNetFromOnnx(onnxPath);
        // CPU 后端
        net.SetPreferableBackend(Backend.OPENCV);
        net.SetPreferableTarget(Target.CPU);

        // 2) 读取原图（BGR，与 cv2.imread 一致）
        using var image = Cv2.ImRead(imgPath, ImreadModes.Color);
        if (image.Empty())
        {
            Console.WriteLine($"错误：无法读取图像 {imgPath}");
            return;
        }
        Console.WriteLine($"原图尺寸：{image.Width}x{image.Height}");

        // 3) 预处理：BGR→RGB + Letterbox + Blob
        using var blob = Preprocess(image, InputSize, out float ratio, out float dw, out float dh);
        net.SetInput(blob);

        // 4) 前向推理
        var sw = Stopwatch.StartNew();
        using var output = net.Forward();
        sw.Stop();
        Console.WriteLine($"推理耗时：{sw.Elapsed.TotalMilliseconds:F1} ms");
        Console.WriteLine($"输出张量：dims={output.Dims}, [{output.Size(0)}, {output.Size(1)}, {output.Size(2)}]");

        // 5) 后处理
        var (boxes, scores, classIds) = Postprocess(
            output, image.Width, image.Height, ratio, dw, dh, filterClassId);

        // 6) 画框 + 标签
        for (int i = 0; i < boxes.Length; i++)
            DrawDetections(image, boxes[i], scores[i], classIds[i]);

        // 7) 保存结果
        Cv2.ImWrite(outPath, image);
        Console.WriteLine($"结果已保存：{outPath}");

        // 8) 打印检测结果
        Console.WriteLine($"检测到 {boxes.Length} 个目标：");
        for (int i = 0; i < boxes.Length; i++)
        {
            var b = boxes[i];
            Console.WriteLine(
                $"  [{i}] {ClassNames[classIds[i]]}({classIds[i]}): " +
                $"score={scores[i]:F2}, box=[x={b.X}, y={b.Y}, w={b.Width}, h={b.Height}]");
        }
    }

    /// <summary>
    /// 预处理：BGR→RGB → Letterbox 到 targetSize → 构造 NCHW float32 blob。
    /// 等价 Python preprocess (L86-125)。
    /// </summary>
    private static Mat Preprocess(Mat src, int targetSize, out float ratio, out float dw, out float dh)
    {
        using var rgb = new Mat();
        Cv2.CvtColor(src, rgb, ColorConversionCodes.BGR2RGB);

        using var letterboxed = Letterbox(rgb, targetSize, out ratio, out dw, out dh);

        var blob = CvDnn.BlobFromImage(
            letterboxed,
            scaleFactor: 1.0 / 255.0,
            size: new Size(targetSize, targetSize),
            mean: default,
            swapRB: false,
            crop: false);
        return blob;
    }

    /// <summary>
    /// Letterbox：等比缩放到 ≤ targetSize，居中填充灰边 (114,114,114)。
    /// 等价 Python letterbox (L127-160)。
    /// </summary>
    private static Mat Letterbox(Mat src, int targetSize, out float ratio, out float dw, out float dh)
    {
        int h0 = src.Height, w0 = src.Width;
        float r = Math.Min((float)targetSize / h0, (float)targetSize / w0);
        int newW = (int)Math.Round(w0 * r);
        int newH = (int)Math.Round(h0 * r);

        var resized = new Mat();
        Cv2.Resize(src, resized, new Size(newW, newH), 0, 0, InterpolationFlags.Linear);

        int totalW = targetSize - newW, totalH = targetSize - newH;
        int left = totalW / 2, right = totalW - left;
        int top = totalH / 2, bottom = totalH - top;

        var padded = new Mat();
        Cv2.CopyMakeBorder(resized, padded, top, bottom, left, right,
            BorderTypes.Constant, new Scalar(114, 114, 114));

        resized.Dispose();

        ratio = r;
        dw = left;
        dh = top;
        return padded;
    }

    /// <summary>
    /// 后处理 YOLO11 输出张量 [1, 84, 8400]。
    /// 等价 Python postprocess_yolo11x (L227-295)。
    /// </summary>
    private static (Rect[] Boxes, float[] Scores, int[] ClassIds) Postprocess(
        Mat output, int origW, int origH,
        float ratio, float dw, float dh, int? filterClassId)
    {
        int numClasses = 80;
        int coords = 4;
        int channels = output.Size(1);
        int numAnchors = output.Size(2);
        if (channels != coords + numClasses)
        {
            Console.WriteLine($"警告：输出通道数 {channels} 不等于 4+80，按 {channels - coords} 类处理");
            numClasses = channels - coords;
        }

        var candRects = new List<Rect>(256);
        var candScores = new List<float>(256);
        var candClassIds = new List<int>(256);

        unsafe
        {
            float* p = (float*)output.Data;

            for (int a = 0; a < numAnchors; a++)
            {
                float maxScore = float.NegativeInfinity;
                int maxCls = 0;
                for (int c = 0; c < numClasses; c++)
                {
                    float s = p[(coords + c) * numAnchors + a];
                    if (s > maxScore) { maxScore = s; maxCls = c; }
                }

                if (maxScore < ConfidenceThres) continue;
                if (filterClassId.HasValue && maxCls != filterClassId.Value) continue;

                float cx = p[0 * numAnchors + a];
                float cy = p[1 * numAnchors + a];
                float w = p[2 * numAnchors + a];
                float h = p[3 * numAnchors + a];

                cx = (cx - dw) / ratio;
                cy = (cy - dh) / ratio;
                w /= ratio;
                h /= ratio;

                int left = (int)Math.Round(cx - w / 2f);
                int top = (int)Math.Round(cy - h / 2f);
                int width = (int)Math.Round(w);
                int height = (int)Math.Round(h);
                left = Math.Max(0, Math.Min(left, origW - 1));
                top = Math.Max(0, Math.Min(top, origH - 1));
                width = Math.Max(0, Math.Min(width, origW - left));
                height = Math.Max(0, Math.Min(height, origH - top));

                candRects.Add(new Rect(left, top, width, height));
                candScores.Add(maxScore);
                candClassIds.Add(maxCls);
            }
        }

        if (candRects.Count == 0)
            return (Array.Empty<Rect>(), Array.Empty<float>(), Array.Empty<int>());

        int[] keepIndices = CvDnn.NMSBoxes(
            candRects.ToArray(), candScores.ToArray(),
            ConfidenceThres, IouThres);

        var boxes = new Rect[keepIndices.Length];
        var scores = new float[keepIndices.Length];
        var classIds = new int[keepIndices.Length];
        for (int i = 0; i < keepIndices.Length; i++)
        {
            int idx = keepIndices[i];
            boxes[i] = candRects[idx];
            scores[i] = candScores[idx];
            classIds[i] = candClassIds[idx];
        }
        return (boxes, scores, classIds);
    }

    /// <summary>
    /// 画检测框 + 类别标签。等价 Python draw_detections (L298-334)。
    /// 颜色用固定调色板，与 Python 的随机色板不同（保证结果稳定可比对）。
    /// </summary>
    private static void DrawDetections(Mat img, Rect box, float score, int classId)
    {
        var (r, g, b) = Palette[classId % Palette.Length];
        var color = new Scalar(r, g, b);

        Cv2.Rectangle(img,
            new Point(box.X, box.Y),
            new Point(box.X + box.Width, box.Y + box.Height),
            color, thickness: 2);

        string label = $"{ClassNames[classId]}: {score:F2}";
        var textSize = Cv2.GetTextSize(label, HersheyFonts.HersheySimplex, 0.5, 1);
        int labelX = box.X;
        int labelY = box.Y - 10 > textSize.Height ? box.Y - 10 : box.Y + 10;

        Cv2.Rectangle(img,
            new Point(labelX, labelY - textSize.Height),
            new Point(labelX + textSize.Width, labelY + textSize.Height),
            color, thickness: -1);

        Cv2.PutText(img, label, new Point(labelX, labelY),
            HersheyFonts.HersheySimplex, 0.5, Scalar.Black, thickness: 1);
    }
}