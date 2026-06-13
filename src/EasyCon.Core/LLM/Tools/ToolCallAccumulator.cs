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

        if (delta.Id is not null)
            acc.Id = delta.Id;

        if (delta.Type is not null)
            acc.Type = delta.Type;

        if (delta.Function is not null)
        {
            if (delta.Function.Name is not null)
                acc.Name = delta.Function.Name;

            if (delta.Function.Arguments is not null)
                acc.ArgumentsBuilder.Append(delta.Function.Arguments);
        }
    }

    /// <summary>
    /// 构建最终的完整 ToolCall 列表。
    /// </summary>
    public List<ToolCall> Build()
    {
        var result = new List<ToolCall>(_calls.Count);
        foreach (var (_, acc) in _calls)
        {
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
