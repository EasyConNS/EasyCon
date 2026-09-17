using System.Text;

namespace EasyCon.Core.LLM.Tools;

/// <summary>
/// 流式 tool_calls 增量拼接器。
/// 将多个 ToolCallDelta 分片按 index 归组，最终产出完整的 ToolCall 列表。
/// </summary>
public class ToolCallAccumulator
{
    private readonly SortedList<int, AccumulatedCall> _calls = [];

    /// <summary>
    /// 追加一个流式增量片段。
    /// </summary>
    public void Append(ToolCallDelta delta)
    {
        if (!_calls.TryGetValue(delta.Index, out var acc))
        {
            acc = new AccumulatedCall();
            _calls[delta.Index] = acc;
        }

        // 注意：部分供应商（如 DeepSeek）在后续分片中返回 id="" 和 name=""
        // 而不是 null，因此不能用 is not null 判断，必须排除空字符串。
        // 一旦 id/name 已被首分片设置，后续空字符串不应覆盖。
        if (!string.IsNullOrEmpty(delta.Id))
            acc.Id = delta.Id;

        if (delta.Type is not null)
            acc.Type = delta.Type;

        if (delta.Function is not null)
        {
            if (!string.IsNullOrEmpty(delta.Function.Name))
                acc.Name = delta.Function.Name;

            if (delta.Function.Arguments is not null)
                acc.ArgumentsBuilder.Append(delta.Function.Arguments);
        }
    }

    /// <summary>
    /// 构建最终的完整 ToolCall 列表。
    /// 自动过滤缺少名称或 ID 的残缺工具调用（非标准格式的模型可能产生此类异常 delta）。
    /// </summary>
    public List<ToolCall> Build()
    {
        var result = new List<ToolCall>(_calls.Count);
        foreach (var (_, acc) in _calls)
        {
            // 跳过缺少名称或 ID 的残缺工具调用
            if (string.IsNullOrWhiteSpace(acc.Name) || string.IsNullOrWhiteSpace(acc.Id))
                continue;

            result.Add(new ToolCall
            {
                Id = acc.Id,
                Type = acc.Type,
                Function = new FunctionCall
                {
                    Name = acc.Name,
                    Arguments = acc.ArgumentsBuilder.ToString()
                }
            });
        }
        return result;
    }

    private class AccumulatedCall
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "function";
        public string Name { get; set; } = "";
        public StringBuilder ArgumentsBuilder { get; } = new();
    }
}