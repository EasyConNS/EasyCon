using EasyCon.Core.LLM.Messages;

namespace EasyCon2.Avalonia.Core.AiAgent.Tools;

/// <summary>
/// 工具执行结果，携带状态信息帮助模型决策。
/// </summary>
public class ToolResult
{
    public string Content { get; init; } = "";
    public ToolResultStatus Status { get; init; } = ToolResultStatus.Success;

    /// <summary>
    /// 附加的多模态消息（如图片），由编排层注入对话历史。
    /// 普通工具返回 null，视觉工具（如 get_frame）通过此属性传递图片数据。
    /// </summary>
    public ChatMessage? AttachedMessage { get; init; }

    public static ToolResult Ok(string content, ChatMessage? attachedMessage = null) => new()
    {
        Content = content,
        Status = ToolResultStatus.Success,
        AttachedMessage = attachedMessage
    };

    public static ToolResult Error(string content) => new() { Content = content, Status = ToolResultStatus.Error };
    public static ToolResult Retryable(string content, string hint) => new()
    {
        Content = $"{content}\n[提示] {hint}",
        Status = ToolResultStatus.Retryable
    };
}

public enum ToolResultStatus
{
    Success,
    Error,
    Retryable
}