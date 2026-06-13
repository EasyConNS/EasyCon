using EasyCon.Core.LLM.Tools;

namespace EasyCon.Core.LLM;

/// <summary>
/// 流式响应的增量片段，区分正文、思考内容、工具调用和错误信息。
/// </summary>
public class StreamDelta
{
    /// <summary>
    /// 增量类型。
    /// </summary>
    public DeltaType Type { get; init; }

    /// <summary>
    /// 文本内容（Content/Thinking/Error 类型使用）。
    /// </summary>
    public string Text { get; init; } = "";

    /// <summary>
    /// 工具调用增量（ToolCall 类型使用）。
    /// </summary>
    public ToolCallDelta? ToolCallDelta { get; init; }

    public static StreamDelta Content(string text) => new() { Type = DeltaType.Content, Text = text };
    public static StreamDelta Thinking(string text) => new() { Type = DeltaType.Thinking, Text = text };
    public static StreamDelta ToolCall(ToolCallDelta delta) => new() { Type = DeltaType.ToolCall, ToolCallDelta = delta };
    public static StreamDelta Error(string text) => new() { Type = DeltaType.Error, Text = text };
}

public enum DeltaType
{
    /// <summary>正文片段（delta.content）</summary>
    Content,
    /// <summary>思考/推理片段（delta.reasoning_content / delta.reasoning）</summary>
    Thinking,
    /// <summary>工具调用增量（delta.tool_calls 分片）</summary>
    ToolCall,
    /// <summary>错误信息</summary>
    Error
}
