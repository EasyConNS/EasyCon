using EasyCon.Core.LLM.Tools;

namespace EasyCon2.Avalonia.Core.AiAgent.Tools;

/// <summary>
/// 工具注册中心，管理可用工具并提供按名称分发和 schema 导出。
/// </summary>
public class ToolRegistry
{
    private readonly Dictionary<string, IAiTool> _tools = new(StringComparer.Ordinal);

    /// <summary>
    /// 注册一个工具。
    /// </summary>
    public ToolRegistry Register(IAiTool tool)
    {
        _tools[tool.Name] = tool;
        return this;
    }

    /// <summary>
    /// 是否注册了指定名称的工具。
    /// </summary>
    public bool Contains(string name) => _tools.ContainsKey(name);

    /// <summary>
    /// 按名称获取工具。
    /// </summary>
    public IAiTool? Get(string name) => _tools.TryGetValue(name, out var tool) ? tool : null;

    /// <summary>
    /// 导出 OpenAI tools 数组格式的工具定义列表。
    /// </summary>
    public List<ToolDefinition> ToToolDefinitions()
    {
        var list = new List<ToolDefinition>(_tools.Count);
        foreach (var (_, tool) in _tools)
        {
            list.Add(ToolDefinition.Create(tool.Name, tool.Description, tool.Parameters));
        }
        return list;
    }
}
