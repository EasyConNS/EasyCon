using EasyCon.Core.LLM.Tools;
using System.Collections.Concurrent;

namespace EasyCon.Core.LLM.Agent.Tools;

/// <summary>
/// 工具注册中心，管理可用工具并提供按名称分发和 schema 导出。
/// 并发模型：注册表会被编排器后台线程（ReAct 循环 Get/ToToolDefinitions）与
/// UI 线程（MCP 工具集变更 Register/Unregister）并发访问，必须线程安全。
/// </summary>
public class ToolRegistry
{
    private readonly ConcurrentDictionary<string, IAiTool> _tools = new(StringComparer.Ordinal);

    /// <summary>
    /// 注册一个工具。重名覆盖（后者胜）。
    /// </summary>
    public ToolRegistry Register(IAiTool tool)
    {
        _tools[tool.Name] = tool;
        return this;
    }

    /// <summary>
    /// 按名称注销一个工具。不存在时静默忽略。
    /// </summary>
    public void Unregister(string name) => _tools.TryRemove(name, out _);

    /// <summary>
    /// 按条件批量注销工具。用于 MCP 工具集变更时移除所有旧适配器。
    /// </summary>
    public void Unregister(Predicate<IAiTool> match)
    {
        foreach (var kv in _tools)
        {
            if (match(kv.Value))
                _tools.TryRemove(kv.Key, out _);
        }
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
    /// 快照当前全部工具（名称 → 工具），供 MCP 导出等遍历场景使用。
    /// </summary>
    public IEnumerable<KeyValuePair<string, IAiTool>> Snapshot() => _tools.ToArray();

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