namespace EasyCon.Core.LLM;

/// <summary>
/// EasyCon AI 助手的系统提示词定义。
/// 每次请求自动注入为 messages 首条 system 角色，不存入对话历史。
/// </summary>
public static class SystemPrompts
{
    /// <summary>
    /// 默认系统提示词。
    /// </summary>
    public const string Default = """
        你是 EasyCon（伊机控）的 AI 助手。
        EasyCon 是一个游戏手柄自动化脚本工具，支持脚本编写、图像识别、按键映射等功能。
        请用中文回答问题，回答简洁准确。
        """;
}
