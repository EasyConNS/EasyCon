using EasyCon.Core.Capabilities;
using EasyCon.Core.LLM.Messages;
using EasyCon.Core.LLM.Tools;
using EasyScript;
using System.Text.Json;

namespace EasyCon.Core.LLM.Agent.Tools;

/// <summary>
/// 原子运行时工具：直接驱动手柄输入 / 取帧 / OCR，
/// 让模型具备"看画面 → 按键 → 再看画面"的原子闭环，无需写整段脚本。
/// 依赖只含 Core 能力端口（IPadInput / ICaptureSource / IOcrService），
/// 宿主经 Func 惰性提供能力实例（未连接返回 null），工具不感知任何集成——
/// 与 EvalEcsTool 同一范式，GUI/CLI 可装配同一实现。
/// </summary>
public static class AtomicRuntimeTools
{
    /// <summary>注册全部原子运行时工具。</summary>
    public static void RegisterAll(
        ToolRegistry registry,
        Func<IPadInput?> padProvider,
        Func<ICaptureSource?> captureProvider,
        Func<IOcrService?> ocrProvider)
    {
        registry.Register(new PressButtonTool(padProvider));
        registry.Register(new SetStickTool(padProvider));
        registry.Register(new CaptureFrameTool(captureProvider));
        registry.Register(new OcrFrameTool(ocrProvider, captureProvider));
    }
}

/// <summary>press_button：按键点击（可重复）。按键名在 JSON 边界一次解析为 GamePadKey，此后全程强类型。</summary>
public class PressButtonTool : IAiTool
{
    private readonly Func<IPadInput?> _padProvider;

    public PressButtonTool(Func<IPadInput?> padProvider) => _padProvider = padProvider;

    public string Name => "press_button";

    public string Description =>
        "在 Switch 上按一个实体按键（如 A/B/X/Y/L/R/ZL/ZR/PLUS/TOP/LEFT 等）。" +
        "适合诊断、调试和少量交互；长序列操作请改用脚本。会真实操作设备，执行前需用户确认。";

    public bool RequiresConfirmation => true;

    public JsonSchema Parameters => new()
    {
        Type = "object",
        Properties = new()
        {
            ["key"] = new JsonSchemaProperty { Type = "string", Description = "按键名（A/B/X/Y/L/R/ZL/ZR/MINUS/PLUS/LCLICK/RCLICK/HOME/CAPTURE/TOP/LEFT/RIGHT/UP/DOWN/LS/RS）" },
            ["duration_ms"] = new JsonSchemaProperty { Type = "integer", Description = "单次按住时长（毫秒），默认 100" },
            ["times"] = new JsonSchemaProperty { Type = "integer", Description = "重复次数，默认 1，上限 100" },
            ["interval_ms"] = new JsonSchemaProperty { Type = "integer", Description = "两次之间的间隔（毫秒），默认 100" }
        },
        Required = ["key"]
    };

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        var key = GetString(args, "key");
        if (string.IsNullOrWhiteSpace(key))
            return ToolResult.Error("[错误] 缺少必填参数 key");
        if (!TryParseKey(key, out var gamePadKey, out var keyError))
            return ToolResult.Error($"[错误] {keyError}");

        var pad = _padProvider();
        if (pad is null)
            return ToolResult.Error("[错误] 单片机未连接，无法执行按键");

        var times = GetInt(args, "times", 1);
        if (times < 1 || times > 100)
            return ToolResult.Error($"[错误] 次数超出范围 (1-100): {times}");

        var durationMs = Math.Clamp(GetInt(args, "duration_ms", 100), 1, 10_000);
        var intervalMs = Math.Clamp(GetInt(args, "interval_ms", 100), 1, 10_000);

        try
        {
            for (var i = 0; i < times; i++)
            {
                pad.ClickButtons(gamePadKey, durationMs, ct);
                if (i < times - 1 && intervalMs > 0)
                    await Task.Delay(intervalMs, ct);
            }
            return ToolResult.Ok($"[成功] 已按 {key} × {times}（每次 {durationMs}ms，间隔 {intervalMs}ms）");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"[错误] 按键执行失败: {ex.Message}");
        }
    }

    internal static bool TryParseKey(string key, out GamePadKey parsed, out string error)
    {
        if (Enum.TryParse(key.Trim(), ignoreCase: true, out parsed) && parsed != GamePadKey.None)
        {
            error = "";
            return true;
        }
        parsed = GamePadKey.None;
        error = $"未知按键名: {key}（合法值如 A/B/X/Y/L/R/ZL/ZR/PLUS/TOP/LEFT/LS/RS 等）";
        return false;
    }

    internal static string? GetString(Dictionary<string, JsonElement> args, string name) =>
        args.TryGetValue(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    internal static int GetInt(Dictionary<string, JsonElement> args, string name, int fallback) =>
        args.TryGetValue(name, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var v) ? v : fallback;
}

/// <summary>set_stick：摇杆偏转（持续后自动回中）。</summary>
public class SetStickTool : IAiTool
{
    private readonly Func<IPadInput?> _padProvider;

    public SetStickTool(Func<IPadInput?> padProvider) => _padProvider = padProvider;

    public string Name => "set_stick";

    public string Description =>
        "偏转 Switch 的摇杆（LS=左摇杆 / RS=右摇杆）。坐标 0-255，128 为中心。" +
        "默认持续 duration_ms 后自动回中；duration_ms=0 表示保持不复位。会真实操作设备，执行前需用户确认。";

    public bool RequiresConfirmation => true;

    public JsonSchema Parameters => new()
    {
        Type = "object",
        Properties = new()
        {
            ["key"] = new JsonSchemaProperty { Type = "string", Description = "摇杆名（LS 或 RS）" },
            ["x"] = new JsonSchemaProperty { Type = "integer", Description = "X 偏转（0-255，128 为中心，0=最左，255=最右）" },
            ["y"] = new JsonSchemaProperty { Type = "integer", Description = "Y 偏转（0-255，128 为中心，0=最上，255=最下）" },
            ["duration_ms"] = new JsonSchemaProperty { Type = "integer", Description = "持续时间（毫秒），0 表示保持不复位，默认 300" }
        },
        Required = ["key", "x", "y"]
    };

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        var key = GetString(args, "key");
        if (string.IsNullOrWhiteSpace(key))
            return ToolResult.Error("[错误] 缺少必填参数 key");
        if (!PressButtonTool.TryParseKey(key, out var gamePadKey, out var keyError))
            return ToolResult.Error($"[错误] {keyError}");

        var pad = _padProvider();
        if (pad is null)
            return ToolResult.Error("[错误] 单片机未连接，无法设置摇杆");

        var x = GetInt(args, "x", 128);
        var y = GetInt(args, "y", 128);
        if (x is < 0 or > 255 || y is < 0 or > 255)
            return ToolResult.Error($"[错误] 摇杆坐标超出范围 (0-255): ({x},{y})");

        var durationMs = GetInt(args, "duration_ms", 300);

        try
        {
            pad.SetStick(gamePadKey, (byte)x, (byte)y);
            if (durationMs > 0)
            {
                await Task.Delay(Math.Clamp(durationMs, 1, 30_000), ct);
                pad.SetStick(gamePadKey, 128, 128);  // 回中
            }
            return ToolResult.Ok(durationMs > 0
                ? $"[成功] 摇杆 {key} 已偏转 ({x},{y}) 持续 {durationMs}ms 后回中"
                : $"[成功] 摇杆 {key} 已偏转 ({x},{y})（保持，不复位）");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"[错误] 摇杆执行失败: {ex.Message}");
        }
    }

    private static string? GetString(Dictionary<string, JsonElement> args, string name) => PressButtonTool.GetString(args, name);

    private static int GetInt(Dictionary<string, JsonElement> args, string name, int fallback) => PressButtonTool.GetInt(args, name, fallback);
}

/// <summary>capture_frame：抓取当前画面（半分辨率 JPEG，多模态消息附加给模型）。</summary>
public class CaptureFrameTool : IAiTool
{
    private readonly Func<ICaptureSource?> _captureProvider;

    public CaptureFrameTool(Func<ICaptureSource?> captureProvider) => _captureProvider = captureProvider;

    public string Name => "capture_frame";

    public string Description =>
        "抓取 Switch 当前画面并作为图片返回给模型（需要多模态模型）。" +
        "适合在动作之后确认画面状态。同轮请勿与其它工具混调。";

    public ToolConcurrency Concurrency => ToolConcurrency.Exclusive;

    public bool RequiresVision => true;

    public bool ReturnsImage => true;

    public JsonSchema Parameters => new() { Type = "object", Properties = new() };

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        var capture = _captureProvider();
        if (capture is null)
            return ToolResult.Error("[错误] 视频源未连接或无画面");

        // 取帧 + 首帧等待 + 模型侧压缩统一走 ModelFrame 收口
        var base64 = await ModelFrame.CaptureForModelAsync(capture, ct);
        if (base64 is null)
            return ToolResult.Error("[错误] 视频源未连接或无画面");

        var image = ChatMessage.User(
        [
            ContentPart.FromText("当前视频画面："),
            ContentPart.FromImageBase64("image/jpeg", base64)
        ]);
        return ToolResult.Ok("[成功] 已获取当前画面（见附加图片）", image);
    }
}

/// <summary>ocr_frame：对当前画面指定区域做 OCR（无需多模态模型）。ROI 裁剪由采集能力端口完成。</summary>
public class OcrFrameTool : IAiTool
{
    private readonly Func<IOcrService?> _ocrProvider;
    private readonly Func<ICaptureSource?> _captureProvider;

    public OcrFrameTool(Func<IOcrService?> ocrProvider, Func<ICaptureSource?> captureProvider)
    {
        _ocrProvider = ocrProvider;
        _captureProvider = captureProvider;
    }

    public string Name => "ocr_frame";

    public string Description =>
        "识别 Switch 当前画面上的文字（OCR）。可指定区域（相对 1920x1080 画面），" +
        "不指定则识别全图。返回文字与置信度，不需要多模态模型。";

    public ToolConcurrency Concurrency => ToolConcurrency.Parallel;

    public JsonSchema Parameters => new()
    {
        Type = "object",
        Properties = new()
        {
            ["x"] = new JsonSchemaProperty { Type = "integer", Description = "区域左上角 X（0-1920），默认 0（全图）" },
            ["y"] = new JsonSchemaProperty { Type = "integer", Description = "区域左上角 Y（0-1080），默认 0" },
            ["width"] = new JsonSchemaProperty { Type = "integer", Description = "区域宽（>0 时启用 ROI），默认 0" },
            ["height"] = new JsonSchemaProperty { Type = "integer", Description = "区域高（>0 时启用 ROI），默认 0" },
            ["language"] = new JsonSchemaProperty { Type = "string", Description = "识别语言（如 chi_sim/eng），缺省用引擎默认" }
        }
    };

    public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        var capture = _captureProvider();
        if (capture is null)
            return Task.FromResult(ToolResult.Error("[错误] 视频源未连接或 OCR 服务不可用"));

        // 工具语义：width/height 任一缺省 0 = 不裁 ROI（全图识别）；显式 ROI 原样透传。
        // 全图走语义化 CaptureFullFrame（不用 ModelFrame——压缩伤识别率）
        var width = GetInt(args, "width", 0);
        var height = GetInt(args, "height", 0);
        var png = width > 0 && height > 0
            ? capture.CaptureFrame(GetInt(args, "x", 0), GetInt(args, "y", 0), width, height)
            : capture.CaptureFullFrame();
        if (string.IsNullOrEmpty(png))
            return Task.FromResult(ToolResult.Error("[错误] 视频源未连接或无画面"));

        var ocr = _ocrProvider();
        if (ocr is null)
            return Task.FromResult(ToolResult.Error("[错误] 视频源未连接或 OCR 服务不可用"));

        var language = GetString(args, "language");
        var text = ocr.Recognize(ImageRef.FromBase64(png), new OcrQuery
        {
            Language = string.IsNullOrWhiteSpace(language) ? null : language,
            X = 0,
            Y = 0,
            Width = 0,
            Height = 0  // ROI 已在采集端口内裁剪
        });
        return Task.FromResult(ToolResult.Ok(
            $"[OCR {ocr.Backend} 置信度 {ocr.LastConfidence}%]\n{(string.IsNullOrWhiteSpace(text) ? "(未识别到文字)" : text)}"));
    }

    private static string? GetString(Dictionary<string, JsonElement> args, string name) => PressButtonTool.GetString(args, name);

    private static int GetInt(Dictionary<string, JsonElement> args, string name, int fallback) => PressButtonTool.GetInt(args, name, fallback);
}