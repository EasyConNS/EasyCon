using System.Text.Json;
using EasyCon.Core.LLM.Messages;
using EasyCon.Core.LLM.Tools;
using EasyCon2.Avalonia.Core.Services;

namespace EasyCon2.Avalonia.Core.AiAgent.Tools;

/// <summary>
/// get_frame 工具：获取当前视频帧并以图片形式注入对话上下文，让模型理解画面。
/// 图片数据通过 ToolResult.AttachedMessage 返回，由编排层注入历史。
/// </summary>
public class GetFrameTool : IAiTool
{
    private readonly IToolCallService _service;

    public GetFrameTool(IToolCallService service) => _service = service;

    public string Name => "get_frame";

    public string Description => "截取当前视频画面并让 AI 理解图像内容。调用后 AI 会自动看到当前画面并进行分析。";

    public JsonSchema Parameters => new() { Type = "object" };

    public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        var status = _service.GetDeviceStatus();
        if (!status.IsCaptureConnected)
            return Task.FromResult(ToolResult.Error("视频源未连接，请先连接视频源后再试。"));

        var base64 = _service.GetCurrentFrameBase64();
        if (base64 is null)
            return Task.FromResult(ToolResult.Error("帧获取失败，请检查视频源连接状态。"));

        // 图片消息通过 ToolResult.AttachedMessage 返回，由编排层注入历史
        var imageMessage = ChatMessage.User(
        [
            ContentPart.FromText("当前视频画面："),
            ContentPart.FromImageBase64("image/png", base64)
        ]);

        return Task.FromResult(ToolResult.Ok("已获取当前画面，正在分析...", imageMessage));
    }
}
