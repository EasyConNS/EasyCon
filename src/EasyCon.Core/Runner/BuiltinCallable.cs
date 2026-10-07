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

    public static Value ImplOcrHole(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
    {
        IOcrService? ocr = capabilities.Ocr;
        string? result = ocr switch
        {
            null => null,
            // 旧委托直通：OcrDelegate 自带采集（P3 随洞改造退役）
            DelegateOcrService legacy => legacy.Ocr?.Invoke(
                args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsString()),
            // 通用服务：区域帧在采集侧裁好，识别服务收整图（query 缺省区域）
            _ => OcrRecognize(ocr, capabilities.Capture,
                args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsString()),
        };
        return Value.FromString(result ?? "ERR!!OCR NOT SUPPORT");
    }

    static string OcrRecognize(IOcrService ocr, ICaptureSource? capture, int x, int y, int w, int h, string lang)
    {
        var frame = capture?.CaptureFrame(x, y, w, h);
        return frame == null
            ? "ERR!!OCR NOT SUPPORT"
            : ocr.Recognize(ImageRef.FromBase64(frame), new OcrQuery { Language = lang });
    }

    public static Value ImplRoiHole(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
    {
        var image = args[0].AsString();
        var result = capabilities.Vision?.Crop(image, args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt());
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

    public static Value ImplReadFile(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
        => Value.FromString(Files(capabilities).ReadAllText(args[0].AsString()));

    public static Value ImplWriteFile(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
    {
        Files(capabilities).WriteAllText(args[0].AsString(), args[1].AsString());
        return Value.Void;
    }

    public static Value ImplAppendFile(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
    {
        Files(capabilities).AppendAllText(args[0].AsString(), args[1].AsString());
        return Value.Void;
    }

    public static Value ImplFileExists(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
        => Value.FromBool(Files(capabilities).Exists(args[0].AsString()));

    public static Value ImplOcrConf(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
        => Value.FromInt(capabilities.Ocr?.LastConfidence ?? 0);

    // ============ ONNX 推理实验函数（IInference；Vision 特征位，P5） ============
    // 纯标量协议：原生边界（EcxNativeContext）不携带数组——NET_RUN 返回输出长度，
    // 结果缓存于线程槽，NET_OUT(i) 逐元素读取（脚本同步语义 = 单线程执行）。

    [ThreadStatic] static float[]? _lastNetOutput;

    public static Value ImplNetLoad(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
        => Value.FromInt(capabilities.Inference?.Load(args[0].AsString()) ?? -1);

    public static Value ImplNetRun(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
    {
        IInference? inference = capabilities.Inference;
        if (inference == null)
        {
            _lastNetOutput = null;
            return Value.FromInt(0);
        }

        try
        {
            ScriptArray input = args[1].AsArray();
            var floats = new float[input.Length];
            for (int i = 0; i < input.Length; i++)
                floats[i] = ToFloat(input[i]);

            float[]? output = inference.Run(args[0].AsInt(), floats);
            _lastNetOutput = output;
            return Value.FromInt(output?.Length ?? 0);
        }
        catch
        {
            _lastNetOutput = null;
            return Value.FromInt(0);   // 实验面：非法输入（非数组/未知会话）→ 空输出
        }
    }

    /// <summary>数值元素容错转换（数组字面量/变量可能是任意数值标签）。</summary>
    static float ToFloat(Value v) => v.ToObject() switch
    {
        double d => (float)d,
        int i => i,
        long l => l,
        uint ui => ui,
        ulong ul => ul,
        byte b => b,
        bool bo => bo ? 1f : 0f,
        _ => throw new InvalidCastException("NET_RUN 输入必须为数值数组"),
    };

    public static Value ImplNetOut(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
    {
        var output = _lastNetOutput;
        int index = args[0].AsInt();
        if (output == null || index < 0 || index >= output.Length)
            return Value.FromDouble(0);
        return Value.FromDouble(output[index]);
    }

    /// <summary>
    /// __OCR_INIT__ 洞：旧位置参数签名映射 <see cref="OcrConfig"/>
    /// （lang → Language，dataPath → ModelPath，engineMode/psmode → Options["tess:*"]）。
    /// 未装配 OCR 能力返回 false。
    /// </summary>
    public static Value ImplOcrInitHole(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
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
    /// → 张量句柄。mode: "gray"（[1,1,rh,rw]）| "rgb"（[1,3,rh,rw]，CHW）。失败返回 0。
    /// </summary>
    public static Value ImplNetImage(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
    {
        IInference? inference = capabilities.Inference;
        if (inference == null)
            return Value.FromInt(0);

        try
        {
            var bytes = Convert.FromBase64String(args[0].AsString());
            using var mat = Mat.FromImageData(bytes);
            if (mat.Empty())
                return Value.FromInt(0);

            int rw = args[1].AsInt();
            int rh = args[2].AsInt();
            if (rw <= 0 || rh <= 0)
                return Value.FromInt(0);

            using var resized = new Mat();
            Cv2.Resize(mat, resized, new Size(rw, rh), 0, 0, InterpolationFlags.Linear);
            var mode = args[3].AsString();
            float[] data;
            int[] shape;
            if (mode == "rgb")
            {
                using var rgb = new Mat();
                Cv2.CvtColor(resized, rgb, ColorConversionCodes.BGR2RGB);
                data = new float[rw * rh * 3];
                MarshalPlaneCopy(rgb, data, 0, rw * rh);       // R
                MarshalPlaneCopy(rgb, data, rw * rh, rw * rh); // G
                MarshalPlaneCopy(rgb, data, 2 * rw * rh, rw * rh); // B
                shape = [1, 3, rh, rw];
            }
            else
            {
                using var gray = new Mat();
                Cv2.CvtColor(resized, gray, ColorConversionCodes.BGR2GRAY);
                data = new float[rw * rh];
                MarshalPlaneCopy(gray, data, 0, rw * rh);
                shape = [1, 1, rh, rw];
            }

            return Value.FromInt(inference.HoldTensor(data, shape));
        }
        catch
        {
            return Value.FromInt(0);
        }
    }

    /// <summary>把 CV_8UC1/8UC3 Mat 的单个通道拷入目标数组（interleave 布局 → 平面布局）。</summary>
    static unsafe void MarshalPlaneCopy(Mat source, float[] target, int targetOffset, int planeLength)
    {
        int channels = source.Channels();
        byte* p = (byte*)source.Data;
        for (int i = 0; i < planeLength; i++)
            target[targetOffset + i] = p[i * channels];
    }

    public static Value ImplNetRunH(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
    {
        IInference? inference = capabilities.Inference;
        if (inference == null)
        {
            _lastNetOutput = null;
            return Value.FromInt(0);
        }

        float[]? output = inference.RunHeld(args[0].AsInt(), args[1].AsInt());
        _lastNetOutput = output;
        return Value.FromInt(output?.Length ?? 0);
    }

    public static Value ImplNetScale(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
        => Value.FromInt(capabilities.Inference?.TransformTensor(
            args[0].AsInt(), (float)args[1].AsDouble(), (float)args[2].AsDouble()) ?? 0);

    public static Value ImplNetFree(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
    {
        capabilities.Inference?.FreeTensor(args[0].AsInt());
        return Value.FromInt(0);
    }

    public static Value ImplNetUnload(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
    {
        capabilities.Inference?.Unload(args[0].AsInt());
        return Value.FromInt(0);
    }

    /// <summary>
    /// NET_ROWS：最近一次推理输出的行数（[1,T,C] → T；向量输出 → 1）。无输出历史返回 0。
    /// </summary>
    public static Value ImplNetRows(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
    {
        var shape = capabilities.Inference?.LastOutputShape;
        if (shape == null || shape.Length < 2)
            return Value.FromInt(0);
        return Value.FromInt(shape[1]);
    }

    /// <summary>
    /// NET_ARGMAX(row)：输出第 row 行的最大值列下标（分类模型的逐行类别）。
    /// 无输出历史 / 行越界返回 -1。
    /// </summary>
    public static Value ImplNetArgMax(ReadOnlySpan<Value> args, CapabilitySet capabilities, CancellationToken token)
    {
        var inference = capabilities.Inference;
        var shape = inference?.LastOutputShape;
        var output = _lastNetOutput;
        if (inference == null || shape == null || output == null || shape.Length < 3)
            return Value.FromInt(-1);

        int cols = shape[2];
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
            (BuiltinFunctions.StrEncode, new DelegateCallable(ImplStrEncode)),
            (BuiltinFunctions.Jq, new DelegateCallable(ImplJq)),
            // 文件 IO
            (BuiltinFunctions.FOpen, new DelegateCallable(ImplFOpen)),
            (BuiltinFunctions.FRead, new DelegateCallable(ImplFRead)),
            (BuiltinFunctions.FWrite, new DelegateCallable(ImplFWrite)),
            (BuiltinFunctions.FClose, new DelegateCallable(ImplFClose)),
            (BuiltinFunctions.FEof, new DelegateCallable(ImplFEof)),
            (BuiltinFunctions.ReadFile, new DelegateCallable(ImplReadFile)),
            (BuiltinFunctions.WriteFile, new DelegateCallable(ImplWriteFile)),
            (BuiltinFunctions.AppendFile, new DelegateCallable(ImplAppendFile)),
            (BuiltinFunctions.FileExists, new DelegateCallable(ImplFileExists)),
            (BuiltinFunctions.OcrConf, new DelegateCallable(ImplOcrConf)),
            // ONNX 推理实验
            (BuiltinFunctions.NetLoad, new DelegateCallable(ImplNetLoad)),
            (BuiltinFunctions.NetRun, new DelegateCallable(ImplNetRun)),
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
            (BuiltinFunctions.OcrHole, new DelegateCallable(ImplOcrHole)),
            (BuiltinFunctions.RoiHole, new DelegateCallable(ImplRoiHole)),
            (BuiltinFunctions.OcrInitHole, new DelegateCallable(ImplOcrInitHole)),
            (BuiltinFunctions.NetImageHole, new DelegateCallable(ImplNetImage)),
            (BuiltinFunctions.NetRunH, new DelegateCallable(ImplNetRunH)),
            (BuiltinFunctions.NetScale, new DelegateCallable(ImplNetScale)),
            (BuiltinFunctions.NetFree, new DelegateCallable(ImplNetFree)),
            (BuiltinFunctions.NetUnload, new DelegateCallable(ImplNetUnload)),
            (BuiltinFunctions.NetArgMax, new DelegateCallable(ImplNetArgMax)),
            (BuiltinFunctions.NetRows, new DelegateCallable(ImplNetRows)),

        ];
    }
}