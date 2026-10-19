using EasyCon.Core.Capabilities;
using EasyCon.Script.Binding;
using EasyCon.Script.Runtime;
using EasyCon.Script.Symbols;
using EasyScript;
using OpenCvSharp;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace EasyCon.Core.Runner;

/// <summary>
/// L3 名表原生的宿主实现（由 EcxVm.BuiltinMap 按名分发）：
/// 采集洞（__CAPTURE__ 系）、ENCODE/JQ、OCR_CONF，以及 L2 文件族 syscall 转发而来的
/// 文件实现（规范名见 EcsSyscall.Names，落 <see cref="IFileSystem"/>）。
/// L2 其余平台 syscall 的语义在 EcxHost.ReferenceSyscall，不经本类。
/// </summary>
internal static class BuiltinCallable
{
    public static Value InvokeAudio(string name, ReadOnlySpan<Value> args, IAudioSession? session, CancellationToken token)
    {
        if (session == null)
            throw new InvalidOperationException("当前宿主未提供脚本音频能力 AUDIO_LOAD/AUDIO_SCORE/AUDIO_UNLOAD。");
        switch (name)
        {
            case "AUDIO_LOAD":
                session.Load(args[0].AsString(), token);
                return Value.Void;
            case "AUDIO_SCORE":
                return Value.FromDouble(session.Score(args[0].AsString(), token));
            case "AUDIO_UNLOAD":
                session.Unload(args[0].AsString(), token);
                return Value.Void;
            default:
                throw new InvalidOperationException($"未知脚本音频函数：{name}。");
        }
    }

    public static Value ImplJq(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
    {
        try
        {
            var json = JsonDocument.Parse(args[0].AsString()).RootElement;
            var query = args[1].AsString();

            var current = json;
            var i = 0;
            while (i < query.Length)
            {
                if (query[i] == '.')
                {
                    i++;
                    var start = i;
                    while (i < query.Length && query[i] != '.' && query[i] != '[')
                        i++;
                    current = current.GetProperty(query[start..i]);
                }
                else if (query[i] == '[')
                {
                    i++;
                    var start = i;
                    while (query[i] != ']')
                        i++;
                    current = current[int.Parse(query[start..i])];
                    i++;
                }
                else
                {
                    i++;
                }
            }

            return Value.FromString(current.ValueKind switch
            {
                JsonValueKind.Number => current.GetRawText(),
                JsonValueKind.String => current.GetString()!,
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Array => current.GetRawText(),
                _ => current.GetRawText(),
            });
        }
        catch
        {
            return Value.FromString("");
        }
    }

    public static Value ImplStrEncode(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
    {
        var array = args[0].AsArray();
        var bytes = new byte[array.Length];
        for (int i = 0; i < array.Length; i++)
            bytes[i] = array[i].AsByte();
        return args[1].AsString() switch
        {
            "unicode" => Encoding.Unicode.GetString(bytes),
            "utf8" or _ => Encoding.UTF8.GetString(bytes)
        };
    }

    // --- 采集卡洞函数 ---

    public static Value ImplCaptureHole(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
    {
        var result = capabilities.Capture?.CaptureFrame(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt());
        return Value.FromString(result ?? "ERR!!FRAME NOT SUPPORT");
    }

    /// <summary>__TESSERACT__ 洞：保持旧名字，OCR 后端由宿主装配。
    /// ppocr 不经此面——走 NET 族张量原语 + std 配方（StdLib PP_OCR_DIGITS）。</summary>
    public static Value ImplTesseractHole(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
    {
        IOcrService? ocr = capabilities.Ocr;
        string? result = ocr switch
        {
            null => null,
            // 旧委托直通：OcrDelegate 自带采集（P3 随洞改造退役）
            DelegateOcrService legacy => legacy.Ocr?.Invoke(
                args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsString()),
            // 通用服务持有 ROI 规则；不能用 FRAME 的零尺寸规则提前裁掉 OCR 输入。
            _ => OcrRecognize(ocr, capabilities.Capture,
                args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsString()),
        };
        return Value.FromString(result ?? "ERR!!OCR NOT SUPPORT");
    }

    static string OcrRecognize(IOcrService ocr, ICaptureSource? capture, int x, int y, int w, int h, string lang)
    {
        var frame = capture?.CaptureFullFrame();
        return frame == null
            ? "ERR!!OCR NOT SUPPORT"
            : ocr.Recognize(ImageRef.FromBase64(frame), new OcrQuery { X = x, Y = y, Width = w, Height = h, Language = lang });
    }

    /// <summary>Vision 能力缺省回落桌面参考实现（同 Files → DesktopFileSystem 先例）：
    /// __ROI__ 与采集路径共享 CropToBase64 裁剪核，桌面零装配即可用。</summary>
    static IVisionService Vision(CapabilitySet capabilities)
        => capabilities.Vision ?? DesktopVisionService.Instance;

    public static Value ImplRoiHole(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
    {
        var image = args[0].AsString();
        var result = Vision(capabilities).Crop(image, args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt());
        return Value.FromString(result ?? "ERR!!ROI NOT SUPPORT");
    }

    // ============ 文件 IO（L2 文件族 syscall 转发落点，语义在 IFileSystem） ============

    public static Value ImplFOpen(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
        => Value.FromPtr(Files(capabilities).Open(args[0].AsString(), args[1].AsString()));

    public static Value ImplFRead(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
        => Value.FromString(Files(capabilities).Read(args[0].AsPtr(), args[1].AsInt()));

    public static Value ImplFWrite(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
        => Value.FromInt(Files(capabilities).Write(args[0].AsPtr(), args[1].AsString()));

    public static Value ImplFClose(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
    {
        Files(capabilities).Close(args[0].AsPtr());
        return Value.Void;
    }

    public static Value ImplFEof(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
        => Value.FromBool(Files(capabilities).Eof(args[0].AsPtr()));

    public static Value ImplFileExists(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
        => Value.FromBool(Files(capabilities).Exists(args[0].AsString()));

    public static Value ImplOcrConf(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
        => Value.FromInt(capabilities.Ocr?.LastConfidence ?? 0);

    // ============ ONNX 推理函数（IInference；Vision 特征位） ============
    // 纯标量协议：原生边界（EcxNativeContext）不携带数组——NET_RUNH 返回输出长度，
    // 结果缓存于线程槽，NET_OUT(i) 逐元素读取（脚本同步语义 = 单线程执行）。

    [ThreadStatic] static float[]? _lastNetOutput;

    // 最近一次 "letter" 模式 __NET_IMAGE__ 的内容区尺寸（缩放后图像在画布中的 newW×newH）。
    // 与 _lastNetOutput 同一"最近配对"线程槽协议：__YOLO_DECODE__ 用它把端到端框
    // 裁剪回内容区（Ultralytics predict 后处理同约定），无需拓宽原生 ABI 实参窗。
    [ThreadStatic] static int _letterContentW;
    [ThreadStatic] static int _letterContentH;

    public static Value ImplNetLoad(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
        => Value.FromPtr(capabilities.Inference?.Load(args[0].AsString()) ?? -1);

    public static Value ImplNetOut(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
    {
        var output = _lastNetOutput;
        int index = args[0].AsInt();
        if (output == null || index < 0 || index >= output.Length)
            return Value.FromDouble(0);
        return Value.FromDouble(output[index]);
    }

    /// <summary>
    /// __TESSERACT_INIT__ 洞：旧位置参数签名映射 <see cref="OcrConfig"/>
    /// （lang → Language，dataPath → ModelPath，engineMode/psmode → Options["tess:*"]）。
    /// 未装配 OCR 能力返回 false。
    /// </summary>
    public static Value ImplTesseractInitHole(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
    {
        var cfg = new OcrConfig
        {
            Language = args[0].AsString(),
            ModelPath = args[1].AsString(),
            Options = new Dictionary<string, string>
            {
                ["tess:engineMode"] = args[2].AsString(),
                ["tess:psmode"] = args[3].AsString(),
            },
        };
        return Value.FromBool(capabilities.Ocr?.Init(cfg) ?? false);
    }

    // ---- NET 句柄族（NET_IMAGE / NET_RUNH / NET_SCALE / NET_FREE / NET_UNLOAD）----
    // 大数组驻留宿主（IInference 张量表），脚本只见句柄；out 参数仍是扁平输出，
    // 复用 _lastNetOutput 线程槽与 NET_OUT(i) 读取。

    /// <summary>
    /// __NET_IMAGE__ 洞：base64 图像 → 缩放 (rw×rh) → 灰度/RGB 平铺 float（0-255 原始值）
    /// → 张量句柄。mode: "gray"（[1,1,rh,rw]）| "rgb"（[1,3,rh,rw]，CHW）|
    /// "letter"（YOLO letterbox：等比缩放至目标内、居中、垫 114 灰，走 RGB 通道），
    /// 可加 'c' 后缀（"rgbc"/"grayc"）改为 cover 缩放 + 居中裁剪（分类模型常规：
    /// 不变形不垫边；letter 无裁剪变体）。letter 的缩放/取整公式与 std YOLO_DETECT
    /// 逆映射同式（零漂移由 YoloDetectTests 锁定）。失败返回 0。
    /// </summary>
    public static Value ImplNetImage(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
    {
        IInference? inference = capabilities.Inference;
        if (inference == null)
            return Value.FromPtr(0);

        try
        {
            return NetImageFromBytes(Convert.FromBase64String(args[0].AsString()),
                args[1].AsInt(), args[2].AsInt(), args[3].AsString(), inference);
        }
        catch
        {
            return Value.FromPtr(0);
        }
    }

    /// <summary>
    /// __NET_IMAGE_FILE__ 洞：本地图像文件 → 同 __NET_IMAGE__ 管线（mode 词表相同）。
    /// 文件读取走 System.IO——与 DesktopFileSystem 参考实现同一沙箱姿态。失败返回 0。
    /// </summary>
    public static Value ImplNetImageFile(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
    {
        IInference? inference = capabilities.Inference;
        if (inference == null)
            return Value.FromPtr(0);

        try
        {
            return NetImageFromBytes(File.ReadAllBytes(args[0].AsString()),
                args[1].AsInt(), args[2].AsInt(), args[3].AsString(), inference);
        }
        catch
        {
            return Value.FromPtr(0);
        }
    }

    /// <summary>图像字节 → 缩放（stretch / letter / crop）→ CHW float（0-255 原始值）→ 张量句柄。</summary>
    static Value NetImageFromBytes(byte[] bytes, int rw, int rh, string mode, IInference inference)
    {
        try
        {
            using var mat = Mat.FromImageData(bytes);
            if (mat.Empty() || rw <= 0 || rh <= 0)
                return Value.FromPtr(0);

            string baseMode = mode;
            bool crop = baseMode.EndsWith('c');
            if (crop)
                baseMode = baseMode[..^1];
            if (crop && baseMode == "letter")
                return Value.FromPtr(0);   // letter 自带垫边，无裁剪变体

            using var resized = new Mat();
            if (baseMode == "letter")
            {
                double scale = Math.Min((double)rw / mat.Width, (double)rh / mat.Height);
                int newW = (int)(mat.Width * scale + 0.5);
                int newH = (int)(mat.Height * scale + 0.5);
                using var scaled = new Mat();
                Cv2.Resize(mat, scaled, new Size(newW, newH), 0, 0, InterpolationFlags.Linear);
                resized.Create(rh, rw, MatType.CV_8UC3);
                resized.SetTo(new Scalar(114, 114, 114));
                scaled.CopyTo(resized[new Rect((rw - newW) / 2, (rh - newH) / 2, newW, newH)]);
                _letterContentW = newW;
                _letterContentH = newH;
            }
            else if (crop)
            {
                double scale = Math.Max((double)rw / mat.Width, (double)rh / mat.Height);
                int newW = (int)(mat.Width * scale + 0.5);
                int newH = (int)(mat.Height * scale + 0.5);
                using var canvas = new Mat();
                Cv2.Resize(mat, canvas, new Size(newW, newH), 0, 0, InterpolationFlags.Linear);
                canvas[new Rect((newW - rw) / 2, (newH - rh) / 2, rw, rh)].CopyTo(resized);
                _letterContentW = 0;
                _letterContentH = 0;
            }
            else
            {
                Cv2.Resize(mat, resized, new Size(rw, rh), 0, 0, InterpolationFlags.Linear);
                _letterContentW = 0;
                _letterContentH = 0;
            }

            float[] data;
            int[] shape;
            if (baseMode == "gray")
            {
                using var gray = new Mat();
                Cv2.CvtColor(resized, gray, ColorConversionCodes.BGR2GRAY);
                data = new float[rw * rh];
                MarshalPlaneCopy(gray, data, 0, rw * rh, 0);
                shape = [1, 1, rh, rw];
            }
            else
            {
                // "rgb"（letter / crop 变体亦走 RGB：YOLO 与分类模型的输入约定）
                using var rgb = new Mat();
                Cv2.CvtColor(resized, rgb, ColorConversionCodes.BGR2RGB);
                data = new float[rw * rh * 3];
                MarshalPlaneCopy(rgb, data, 0, rw * rh, 0);          // R
                MarshalPlaneCopy(rgb, data, rw * rh, rw * rh, 1);    // G
                MarshalPlaneCopy(rgb, data, 2 * rw * rh, rw * rh, 2); // B
                shape = [1, 3, rh, rw];
            }

            return Value.FromPtr(inference.HoldTensor(data, shape));
        }
        catch
        {
            return Value.FromPtr(0);
        }
    }

    /// <summary>把 CV_8UC1/8UC3 Mat 的单个通道拷入目标数组（interleave 布局 → 平面布局）。</summary>
    static unsafe void MarshalPlaneCopy(Mat source, float[] target, int targetOffset, int planeLength, int sourceChannel)
    {
        int channels = source.Channels();
        byte* p = (byte*)source.Data;
        for (int i = 0; i < planeLength; i++)
            target[targetOffset + i] = p[i * channels + sourceChannel];
    }

    public static Value ImplNetRunH(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
    {
        IInference? inference = capabilities.Inference;
        if (inference == null)
        {
            _lastNetOutput = null;
            return Value.FromInt(0);
        }

        float[]? output = inference.RunHeld((int)args[0].AsPtr(), (int)args[1].AsPtr());
        _lastNetOutput = output;
        return Value.FromInt(output?.Length ?? 0);
    }

    public static Value ImplNetScale(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
        => Value.FromPtr(capabilities.Inference?.TransformTensor(
            (int)args[0].AsPtr(), (float)args[1].AsDouble(), (float)args[2].AsDouble()) ?? 0);

    public static Value ImplNetFree(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
    {
        capabilities.Inference?.FreeTensor((int)args[0].AsPtr());
        return Value.FromInt(0);
    }

    public static Value ImplNetUnload(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
    {
        capabilities.Inference?.Unload((int)args[0].AsPtr());
        return Value.FromInt(0);
    }

    /// <summary>
    /// NET_ROWS：最近一次推理输出的行数（末维 = 类别维：rows = 前 n-1 维之积）。
    /// [1,T,C] → T；向量输出 [1,N] → 1。无输出历史返回 0。
    /// </summary>
    public static Value ImplNetRows(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
    {
        var shape = capabilities.Inference?.LastOutputShape;
        if (shape == null || shape.Length < 1 || shape[^1] <= 0)
            return Value.FromInt(0);
        long rows = 1;
        for (int i = 0; i < shape.Length - 1; i++)
        {
            if (shape[i] <= 0)
                return Value.FromInt(0);
            rows *= shape[i];
        }
        return Value.FromInt(rows > int.MaxValue ? int.MaxValue : (int)rows);
    }

    /// <summary>
    /// NET_ARGMAX(row)：输出第 row 行的最大值列下标（分类模型的逐行类别）。
    /// 末维 = 类别维：[1,T,C] 按 T 行 C 列；向量输出 [1,N]（或 [N]）按 1 行 N 列。
    /// 无输出历史 / 行越界返回 -1。
    /// </summary>
    public static Value ImplNetArgMax(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
    {
        var inference = capabilities.Inference;
        var shape = inference?.LastOutputShape;
        var output = _lastNetOutput;
        if (inference == null || shape == null || output == null || shape.Length < 1)
            return Value.FromInt(-1);

        int cols = shape[^1];
        long rows = cols > 0 ? output.LongLength / cols : 0;
        int row = args[0].AsInt();
        if (cols <= 0 || row < 0 || row >= rows)
            return Value.FromInt(-1);

        int best = 0;
        float bestVal = float.MinValue;
        int baseIdx = row * cols;
        for (int c = 0; c < cols; c++)
        {
            if (output[baseIdx + c] > bestVal)
            {
                bestVal = output[baseIdx + c];
                best = c;
            }
        }
        return Value.FromInt(best);
    }

    /// <summary>
    /// __CTC_FOLD__ 洞：CTC 贪心折叠最近一次 NET_RUNH 输出。宿主不做形状假设——
    /// 按末维约定解释（cols = 末维，rows = 元素数/cols；[1,T,C] 即 T 帧 C 列）。
    /// 逐帧 argmax 后：连续同类别去重、blank 帧重置、charset 窗口 [base, base+len) 外的
    /// 类别跳过（仍参与去重序列）；窗口内类别 c 输出 charset[c-base]。一次宿主遍历，
    /// 无逐帧 VM 往返。配方语义（ppocr 字典下标、数字子集）由 std CTC_FOLD 传入；
    /// 自实现解码策略走 NET_ROWS + NET_ARGMAX 逐帧遍历。
    /// </summary>
    public static Value ImplCtcFoldHole(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
    {
        var shape = capabilities.Inference?.LastOutputShape;
        var output = _lastNetOutput;
        if (shape == null || output == null || shape.Length < 1 || shape[^1] <= 0)
            return Value.FromString("");

        int blank = args[0].AsInt();
        var charset = args[1].AsString();
        int baseId = args[2].AsInt();
        int cols = shape[^1];
        long rows = output.LongLength / cols;

        var sb = new StringBuilder();
        int prev = -1;
        for (int row = 0; row < rows; row++)
        {
            int best = 0;
            float bestVal = float.MinValue;
            int baseIdx = row * cols;
            for (int c = 0; c < cols; c++)
            {
                if (output[baseIdx + c] > bestVal)
                {
                    bestVal = output[baseIdx + c];
                    best = c;
                }
            }
            if (best == blank)
            {
                prev = -1;
            }
            else
            {
                if (best != prev && best >= baseId && best < baseId + charset.Length)
                    sb.Append(charset[best - baseId]);
                prev = best;
            }
        }
        return Value.FromString(sb.ToString());
    }

    /// <summary>
    /// NET_NORM(tensor, mean, std)：逐通道归一化 (x - mean[c]) / std[c]，通道号由宿主按
    /// 张量形状推断（IInference.Normalize）。返回新句柄，失败 -1。/255 不烧进来——
    /// 量程折叠由 NET_SCALE 链式承担，每步都是通用原语（预处理策略留在脚本层）。
    /// </summary>
    public static Value ImplNetNorm(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
    {
        var inference = capabilities.Inference;
        if (inference == null)
            return Value.FromPtr(-1);
        try
        {
            var meanArr = args[1].AsArray();
            var mean = new float[meanArr.Length];
            for (int i = 0; i < meanArr.Length; i++)
                mean[i] = (float)meanArr[i].AsDouble();
            var stdArr = args[2].AsArray();
            var std = new float[stdArr.Length];
            for (int i = 0; i < stdArr.Length; i++)
                std[i] = (float)stdArr[i].AsDouble();
            return Value.FromPtr(inference.Normalize((int)args[0].AsPtr(), mean, std));
        }
        catch
        {
            return Value.FromPtr(-1);
        }
    }

    // ---- 标签表（NET_LABELS / NET_LABEL）：分类结果下标 → 类别名。纯字符串处理，
    //      不挂 Vision 特征位；表驻宿主静态槽（与 _lastNetOutput 同款会话内共享）----

    static readonly object _labelGate = new();
    static string[] _labels = [];

    /// <summary>
    /// NET_LABELS(path)：加载标签文件，返回标签数，失败 -1（含读失败与空文件）；重复加载整体替换。
    /// 字典级语义：行号即索引——除文件末尾换行的幻影项外逐行原样占位（空行/空白字符行
    /// 也是条目，如 PP-OCR 字典首行的全角空格），仅去行尾 CR。加锁保护。
    /// </summary>
    public static Value ImplNetLabels(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
    {
        var text = Files(capabilities).ReadAllText(args[0].AsString());
        if (text.Length == 0)
            return Value.FromInt(-1);   // 读失败（IFileSystem 契约 = 空串）与空文件同为加载失败

        var lines = text.Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0)
            lines = lines[..^1];        // 结尾换行的幻影空项（真实空行占位不动）
        var labels = new string[lines.Length];
        for (int i = 0; i < lines.Length; i++)
            labels[i] = lines[i].EndsWith('\r') ? lines[i][..^1] : lines[i];
        lock (_labelGate)
            _labels = labels;
        return Value.FromInt(labels.Length);
    }

    /// <summary>NET_LABEL(index)：第 index 个标签；未加载 / 越界返回 ""。</summary>
    public static Value ImplNetLabel(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
    {
        lock (_labelGate)
        {
            int i = args[0].AsInt();
            return i >= 0 && i < _labels.Length ? Value.FromString(_labels[i]) : Value.FromString("");
        }
    }

    // ---- YOLO26 端到端解码（__YOLO_DECODE__ 洞 + YOLO_* 访问器）----
    // 读最近一次 NET_RUNH 输出（端到端导出 [·,300,6] = x1,y1,x2,y2,conf,cls，
    // letterbox 像素空间；argmax 已由模型内部完成——列语义异质，NET_ARGMAX 不适用），
    // 逐行按置信度过滤，经 (v - pad)/scale + roi 逆映射回屏幕坐标缓存命中列表。
    // 逆映射参数由 std YOLO_DETECT 传入（配方持语义）；自实现解码策略走
    // NET_ROWS + NET_OUT(i*6+j) 扁平寻址。

    [ThreadStatic] static List<float[]>? _yoloHits;

    /// <summary>
    /// __YOLO_DECODE__ 洞：按 (thresh, scale, padX, padY, roiX, roiY) 解码最近输出。
    /// 宿主不做形状假设——按末维约定解释（cols = 末维，rows = 元素数/cols），
    /// 逐行按置信度过滤，先裁剪回 letterbox 内容区再逆映射（内容区尺寸取自最近
    /// "letter" __NET_IMAGE__ 线程槽；Ultralytics predict 后处理同约定）。
    /// 端到端 stride 校验（末维 = 6）由 std YOLO_DETECT 承担。返回命中数；
    /// 输出缺失或 scale ≤ 0 返回 -1。
    /// </summary>
    public static Value ImplYoloDecode(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
    {
        var hits = _yoloHits ??= [];
        hits.Clear();

        var shape = capabilities.Inference?.LastOutputShape;
        var output = _lastNetOutput;
        if (shape == null || output == null || shape.Length < 1)
            return Value.FromInt(-1);

        float thresh = (float)args[0].AsDouble();
        float scale = (float)args[1].AsDouble();
        if (scale <= 0)
            return Value.FromInt(-1);
        float padX = (float)args[2].AsDouble();
        float padY = (float)args[3].AsDouble();
        float roiX = args[4].AsInt();
        float roiY = args[5].AsInt();
        float contentW = _letterContentW;
        float contentH = _letterContentH;

        int cols = shape[^1];
        long rows = cols > 0 ? output.LongLength / cols : 0;
        for (long i = 0; i < rows; i++)
        {
            long b = i * cols;
            float score = output[b + 4];
            if (score < thresh)
                continue;
            float Map(float v, float pad, float roi, float content)
            {
                if (content > 0)
                    v = Math.Clamp(v, pad, pad + content);
                return (v - pad) / scale + roi;
            }
            hits.Add(
            [
                Map(output[b + 0], padX, roiX, contentW),
                Map(output[b + 1], padY, roiY, contentH),
                Map(output[b + 2], padX, roiX, contentW),
                Map(output[b + 3], padY, roiY, contentH),
                score,
                output[b + 5],
            ]);
        }
        return Value.FromInt(hits.Count);
    }

    static List<float[]> YoloHits() => _yoloHits ?? [];

    /// <summary>YOLO_COUNT()：最近一次解码的命中数（未解码过为 0）。</summary>
    public static Value ImplYoloCount(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
        => Value.FromInt(YoloHits().Count);

    /// <summary>YOLO_* 访问器：索引最近命中列表；越界/未解码返回 -1（分数为 -1.0）。</summary>
    public static Value ImplYoloCls(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
    {
        var hits = YoloHits();
        int i = args[0].AsInt();
        return i >= 0 && i < hits.Count ? Value.FromInt((int)hits[i][5]) : Value.FromInt(-1);
    }

    public static Value ImplYoloScore(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
    {
        var hits = YoloHits();
        int i = args[0].AsInt();
        return i >= 0 && i < hits.Count ? Value.FromDouble(hits[i][4]) : Value.FromDouble(-1.0);
    }

    static Value YoloCoord(ReadOnlySpan<Value> args, int field)
    {
        var hits = YoloHits();
        int i = args[0].AsInt();
        return i >= 0 && i < hits.Count
            ? Value.FromInt((int)MathF.Round(hits[i][field], MidpointRounding.AwayFromZero))
            : Value.FromInt(-1);
    }

    public static Value ImplYoloX1(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token) => YoloCoord(args, 0);
    public static Value ImplYoloY1(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token) => YoloCoord(args, 1);
    public static Value ImplYoloX2(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token) => YoloCoord(args, 2);
    public static Value ImplYoloY2(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token) => YoloCoord(args, 3);

    // ---- PP-OCR det 找框（__DET_BOXES__ 洞 + DET_* 访问器）----
    // 读最近一次 NET_RUNH 输出的扁平概率数据，阈值二值化 + 连通域外轮廓 → 框列表
    // （det 空间滤噪 <3px → 逆映射 screen = det/scale + roi → ±2px unclip 扩张），
    // 阅读序（y 优先 x 次之）缓存。宿主不做任何形状假设：概率图几何 (mapW×mapH)
    // 由 std PP_DET_FIND 传入（即配方自知的 det 输入尺寸）；自实现解码策略走
    // 概率图原语另行遍历。

    [ThreadStatic] static List<int[]>? _detBoxes;

    /// <summary>
    /// __DET_BOXES__ 洞：按 (thresh, scale, roiX, roiY, mapW, mapH) 在最近概率图上找文本框。
    /// 宿主不做任何形状假设——几何参数由配方传入，洞只按参数读取扁平数组；
    /// 输出缺失、scale ≤ 0 或数据不足（模型输出与配方几何不符，防越界）返回 -1。
    /// </summary>
    public static Value ImplDetBoxes(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
    {
        var boxes = _detBoxes ??= [];
        boxes.Clear();

        var output = _lastNetOutput;
        if (output == null)
            return Value.FromInt(-1);

        double thresh = args[0].AsDouble();
        double scale = args[1].AsDouble();
        if (scale <= 0)
            return Value.FromInt(-1);
        int roiX = args[2].AsInt();
        int roiY = args[3].AsInt();
        int w = args[4].AsInt();
        int h = args[5].AsInt();
        if (w <= 0 || h <= 0 || output.LongLength < (long)w * h)
            return Value.FromInt(-1);   // 数据不足防越界，非布局假设

        using var prob = Mat.FromPixelData(h, w, MatType.CV_32FC1, output);
        using var bin = new Mat();
        Cv2.Threshold(prob, bin, thresh, 255, ThresholdTypes.Binary);
        using var bin8 = new Mat();
        bin.ConvertTo(bin8, MatType.CV_8UC1);
        // 水平膨胀（1×15 核）：同一文字行内的数字/单词间隙愈合，碎框合并为整行框
        // （DB 后处理标准手法；横排 UI 假设——竖排文本行内间隙不愈合，v1 不支持）
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(15, 1));
        Cv2.Dilate(bin8, bin8, kernel);
        Cv2.FindContours(bin8, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        foreach (var contour in contours)
        {
            var r = Cv2.BoundingRect(contour);
            if (r.Width < 3 || r.Height < 3)   // det 空间噪声过滤
                continue;
            // unclip（PaddleOCR 同式）：offset = area × ratio / perimeter，v1 ratio=1.0——
            // DB 框贴字紧缩，rec 需要留白。ratio=2.0（官方上限）在整页 UI 截图上会把大框
            // 扩成数千像素宽的 rec 输入，DNN 进程崩溃——上调 ratio 前必须先做框尺寸护栏
            double offset = 1.0 * r.Width * r.Height / (2.0 * (r.Width + r.Height));
            int dx = (int)Math.Round(offset / scale, MidpointRounding.AwayFromZero);
            boxes.Add(
            [
                (int)Math.Round(r.X / scale, MidpointRounding.AwayFromZero) + roiX - dx,
                (int)Math.Round(r.Y / scale, MidpointRounding.AwayFromZero) + roiY - dx,
                (int)Math.Round(r.Width / scale, MidpointRounding.AwayFromZero) + 2 * dx,
                (int)Math.Round(r.Height / scale, MidpointRounding.AwayFromZero) + 2 * dx,
            ]);
        }
        boxes.Sort((a, b) => a[1] != b[1] ? a[1].CompareTo(b[1]) : a[0].CompareTo(b[0]));
        return Value.FromInt(boxes.Count);
    }

    static List<int[]> DetBoxes() => _detBoxes ?? [];

    /// <summary>DET_COUNT()：最近一次找框的框数（未找过为 0）。</summary>
    public static Value ImplDetCount(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
        => Value.FromInt(DetBoxes().Count);

    static Value DetField(ReadOnlySpan<Value> args, int field)
    {
        var boxes = DetBoxes();
        int i = args[0].AsInt();
        return i >= 0 && i < boxes.Count ? Value.FromInt(boxes[i][field]) : Value.FromInt(-1);
    }

    public static Value ImplDetX(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token) => DetField(args, 0);
    public static Value ImplDetY(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token) => DetField(args, 1);
    public static Value ImplDetW(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token) => DetField(args, 2);
    public static Value ImplDetH(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token) => DetField(args, 3);

    /// <summary>文件能力缺省回落桌面参考实现（FCLOSE 句柄表清理随 DesktopFileSystem.Instance）。</summary>
    static IFileSystem Files(CapabilitySet capabilities)
        => capabilities.Files ?? DesktopFileSystem.Instance;

    /// <summary>
    /// 获取所有保留内置函数及其对应的 Callable。
    /// </summary>
    public static ImmutableArray<(FunctionSymbol Symbol, ICallable Callable)> GetAll()
    {
        return
        [
            (BuiltinFunctions.EncodeHole, new DelegateCallable(ImplStrEncode)),
            (BuiltinFunctions.JqHole, new DelegateCallable(ImplJq)),
            // 文件 IO
            (BuiltinFunctions.FOpen, new DelegateCallable(ImplFOpen)),
            (BuiltinFunctions.FRead, new DelegateCallable(ImplFRead)),
            (BuiltinFunctions.FWrite, new DelegateCallable(ImplFWrite)),
            (BuiltinFunctions.FClose, new DelegateCallable(ImplFClose)),
            (BuiltinFunctions.FEof, new DelegateCallable(ImplFEof)),
            (BuiltinFunctions.FileExists, new DelegateCallable(ImplFileExists)),
            (BuiltinFunctions.OcrConf, new DelegateCallable(ImplOcrConf)),
            // ONNX 推理实验
            (BuiltinFunctions.NetLoad, new DelegateCallable(ImplNetLoad)),
            (BuiltinFunctions.NetOut, new DelegateCallable(ImplNetOut)),
        ];
    }

    /// <summary>
    /// 获取采集卡洞函数的 Callable 映射。
    /// </summary>
    public static ImmutableArray<(FunctionSymbol Symbol, ICallable Callable)> GetCaptureHoleCallables()
    {
        return
        [
            (BuiltinFunctions.CaptureHole, new DelegateCallable(ImplCaptureHole)),
            (BuiltinFunctions.TesseractHole, new DelegateCallable(ImplTesseractHole)),
            (BuiltinFunctions.RoiHole, new DelegateCallable(ImplRoiHole)),
            (BuiltinFunctions.TesseractInitHole, new DelegateCallable(ImplTesseractInitHole)),
            (BuiltinFunctions.NetImageHole, new DelegateCallable(ImplNetImage)),
            (BuiltinFunctions.NetRunH, new DelegateCallable(ImplNetRunH)),
            (BuiltinFunctions.NetScale, new DelegateCallable(ImplNetScale)),
            (BuiltinFunctions.NetFree, new DelegateCallable(ImplNetFree)),
            (BuiltinFunctions.NetUnload, new DelegateCallable(ImplNetUnload)),
            (BuiltinFunctions.NetArgMax, new DelegateCallable(ImplNetArgMax)),
            (BuiltinFunctions.NetRows, new DelegateCallable(ImplNetRows)),
            (BuiltinFunctions.NetNorm, new DelegateCallable(ImplNetNorm)),
            (BuiltinFunctions.NetImageFileHole, new DelegateCallable(ImplNetImageFile)),
            (BuiltinFunctions.NetLabels, new DelegateCallable(ImplNetLabels)),
            (BuiltinFunctions.NetLabel, new DelegateCallable(ImplNetLabel)),
            (BuiltinFunctions.CtcFoldHole, new DelegateCallable(ImplCtcFoldHole)),
            (BuiltinFunctions.YoloDecodeHole, new DelegateCallable(ImplYoloDecode)),
            (BuiltinFunctions.YoloCount, new DelegateCallable(ImplYoloCount)),
            (BuiltinFunctions.YoloCls, new DelegateCallable(ImplYoloCls)),
            (BuiltinFunctions.YoloScore, new DelegateCallable(ImplYoloScore)),
            (BuiltinFunctions.YoloX1, new DelegateCallable(ImplYoloX1)),
            (BuiltinFunctions.YoloY1, new DelegateCallable(ImplYoloY1)),
            (BuiltinFunctions.YoloX2, new DelegateCallable(ImplYoloX2)),
            (BuiltinFunctions.YoloY2, new DelegateCallable(ImplYoloY2)),
            (BuiltinFunctions.DetBoxesHole, new DelegateCallable(ImplDetBoxes)),
            (BuiltinFunctions.DetCount, new DelegateCallable(ImplDetCount)),
            (BuiltinFunctions.DetX, new DelegateCallable(ImplDetX)),
            (BuiltinFunctions.DetY, new DelegateCallable(ImplDetY)),
            (BuiltinFunctions.DetW, new DelegateCallable(ImplDetW)),
            (BuiltinFunctions.DetH, new DelegateCallable(ImplDetH)),

        ];
    }
}