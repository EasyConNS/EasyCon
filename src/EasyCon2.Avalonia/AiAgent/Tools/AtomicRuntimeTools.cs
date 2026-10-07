using EasyCon.Core.LLM.Agent.Tools;
using EasyCon.Core.LLM.Messages;
using EasyCon.Core.LLM.Tools;
using EasyCon2.Avalonia.Services;
using System.Text.Json;

namespace EasyCon2.Avalonia.AiAgent.Tools;

/// <summary>
/// 原子运行时工具：直接驱动手柄输入 / 取帧 / OCR，
/// 让模型具备"看画面 → 按键 → 再看画面"的原子闭环，无需写整段脚本。
/// 每个工具对应一个运行时能力（IPadInput / ICaptureSource / IOcrService）。
/// </summary>
public static class AtomicRuntimeTools
{
    /// <summary>注册全部原子运行时工具。</summary>
    public static void RegisterAll(ToolRegistry registry, IToolCallService service)
    {
        registry.Register(new PressButtonTool(service));
        registry.Register(new SetStickTool(service));
        registry.Register(new CaptureFrameTool(service));
        registry.Register(new OcrFrameTool(service));
    }
}

/// <summary>press_button：按键点击（可重复）。</summary>
public class PressButtonTool : IAiTool
{
    private readonly IToolCallService _service;

    public PressButtonTool(IToolCallService service) => _service = service;

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
            ["key"] = new JsonSchemaProperty { Type = "string", Description = "按键名（A/B/X/Y/L/R/ZL/ZR/MINUS/PLUS/LCLICK/RCLICK/HOME/CAPTURE/TOP/RIGHT/DOWN/LEFT/LS/RS）" },
            ["duration_ms"] = new JsonSchemaProperty { Type = "integer", Description = "单次按住时长（毫秒），默认 100" },
            ["times"] = new JsonSchemaProperty { Type = "integer", Description = "重复次数，默认 1，上限 100" },
            ["interval_ms"] = new JsonSchemaProperty { Type = "integer", Description = "两次之间的间隔（毫秒），默认 100" }
        },
        Required = ["key"]
    };

    public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        var key = GetString(args, "key");
        if (string.IsNullOrWhiteSpace(key))
            return Task.FromResult(ToolResult.Error("[错误] 缺少必填参数 key"));
        var result = _service.PressButton(
            key,
            GetInt(args, "duration_ms", 100),
            GetInt(args, "times", 1),
            GetInt(args, "interval_ms", 100));
        return Task.FromResult(result.Success
            ? ToolResult.Ok($"[成功] {result.Message}")
            : ToolResult.Error($"[错误] {result.Message}"));
    }

    internal static string? GetString(Dictionary<string, JsonElement> args, string name) =>
        args.TryGetValue(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    internal static int GetInt(Dictionary<string, JsonElement> args, string name, int fallback) =>
        args.TryGetValue(name, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var v) ? v : fallback;
}

/// <summary>set_stick：摇杆偏转（持续后自动回中）。</summary>
public class SetStickTool : IAiTool
{
    private readonly IToolCallService _service;

    public SetStickTool(IToolCallService service) => _service = service;

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

    public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        var key = PressButtonTool.GetString(args, "key");
        if (string.IsNullOrWhiteSpace(key))
            return Task.FromResult(ToolResult.Error("[错误] 缺少必填参数 key"));
        var x = PressButtonTool.GetInt(args, "x", 128);
        var y = PressButtonTool.GetInt(args, "y", 128);
        var result = _service.SetStick(key, x, y, PressButtonTool.GetInt(args, "duration_ms", 300));
        return Task.FromResult(result.Success
            ? ToolResult.Ok($"[成功] {result.Message}")
            : ToolResult.Error($"[错误] {result.Message}"));
    }
}

/// <summary>capture_frame：抓取当前画面（半分辨率 JPEG，多模态消息附加给模型）。</summary>
public class CaptureFrameTool : IAiTool
{
    private readonly IToolCallService _service;

    public CaptureFrameTool(IToolCallService service) => _service = service;

    public string Name => "capture_frame";

    public string Description =>
        "抓取 Switch 当前画面并作为图片返回给模型（需要多模态模型）。" +
        "适合在动作之后确认画面状态。同轮请勿与其它工具混调。";

    public ToolConcurrency Concurrency => ToolConcurrency.Exclusive;

    public JsonSchema Parameters => new() { Type = "object", Properties = new() };

    public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        var base64 = _service.GetCurrentFrameBase64();
        if (base64 is null)
            return Task.FromResult(ToolResult.Error("[错误] 视频源未连接或无画面"));
        var image = ChatMessage.User(
        [
            ContentPart.FromText("当前视频画面："),
            ContentPart.FromImageBase64("image/jpeg", base64)
        ]);
        return Task.FromResult(ToolResult.Ok("[成功] 已获取当前画面（见附加图片）", image));
    }
}

/// <summary>ocr_frame：对当前画面指定区域做 OCR（无需多模态模型）。</summary>
public class OcrFrameTool : IAiTool
{
    private readonly IToolCallService _service;

    public OcrFrameTool(IToolCallService service) => _service = service;

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
        var result = _service.OcrFrame(
            PressButtonTool.GetString(args, "language"),
            PressButtonTool.GetInt(args, "x", 0),
            PressButtonTool.GetInt(args, "y", 0),
            PressButtonTool.GetInt(args, "width", 0),
            PressButtonTool.GetInt(args, "height", 0));
        if (result is null)
            return Task.FromResult(ToolResult.Error("[错误] 视频源未连接或 OCR 服务不可用"));
        return Task.FromResult(ToolResult.Ok(
            $"[OCR {result.Backend} 置信度 {result.Confidence}%]\n{(string.IsNullOrWhiteSpace(result.Text) ? "(未识别到文字)" : result.Text)}"));
    }
}