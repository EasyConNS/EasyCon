using EasyCon.Core.Capabilities;
using EasyCon.Core.LLM.Messages;
using EasyCon.Core.LLM.Tools;
using System.Globalization;
using System.Text.Json;

namespace EasyCon.Core.LLM.Agent.Tools;

/// <summary>
/// get_frame 工具：获取当前视频帧并以图片形式注入对话上下文，让模型理解画面。
/// 图片数据通过 ToolResult.AttachedMessage 返回，由编排层注入历史。
/// 依赖只有采集能力端口（编排器按模型视觉能力做 RequiresVision 门控）。
/// </summary>
public class GetFrameTool : IAiTool
{
    private readonly Func<ICaptureSource?> _captureProvider;

    public GetFrameTool(Func<ICaptureSource?> captureProvider) => _captureProvider = captureProvider;

    public string Name => "get_frame";

    public string Description => "截取当前视频画面并让 AI 理解图像内容。调用后 AI 会自动看到当前画面并进行分析。";

    public ToolConcurrency Concurrency => ToolConcurrency.Exclusive;

    public bool RequiresVision => true;

    public bool ReturnsImage => true;

    public JsonSchema Parameters => new() { Type = "object" };

    public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        var capture = _captureProvider();
        if (capture is null)
            return Task.FromResult(ToolResult.Error("视频源未连接，请先连接视频源后再试。"));

        var png = capture.CaptureFrame(0, 0, 0, 0);
        if (string.IsNullOrEmpty(png))
            return Task.FromResult(ToolResult.Error("帧获取失败，请检查视频源连接状态。"));

        var base64 = ModelFrame.EncodeForModel(png);

        // 捕获时刻随结果文本回传，供模型判断帧的新旧（配合"行动后需重新取帧"的编排约定）
        var capturedAt = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

        // 图片消息通过 ToolResult.AttachedMessage 返回，由编排层注入历史
        var imageMessage = ChatMessage.User(
        [
            ContentPart.FromText("当前视频画面："),
            ContentPart.FromImageBase64("image/jpeg", base64)
        ]);

        return Task.FromResult(ToolResult.Ok($"已获取当前画面（捕获于 {capturedAt}），正在分析...", imageMessage));
    }
}