namespace EasyCon2.Avalonia.Core.AiAgent.Tools;

/// <summary>
/// 工具执行结果，携带状态信息帮助模型决策。
/// </summary>
public class ToolResult
{
    public string Content { get; init; } = "";
    public ToolResultStatus Status { get; init; } = ToolResultStatus.Success;

    public static ToolResult Ok(string content) => new() { Content = content, Status = ToolResultStatus.Success };
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
