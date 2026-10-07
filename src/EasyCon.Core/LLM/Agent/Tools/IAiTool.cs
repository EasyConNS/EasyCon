using EasyCon.Core.LLM.Tools;
using System.Text.Json;

namespace EasyCon.Core.LLM.Agent.Tools;

/// <summary>
/// 工具同轮并发资格。编排器据此决定多工具调用是并行成组还是独占串行。
/// </summary>
public enum ToolConcurrency
{
    /// <summary>有副作用：独占执行，不与任何工具同轮并发。</summary>
    Exclusive,

    /// <summary>只读：可与其它只读工具并行。</summary>
    Parallel
}

/// <summary>
/// 通用 AI 工具接口。每个工具负责声明自身 schema 并执行调用。
/// </summary>
public interface IAiTool
{
    /// <summary>
    /// 工具唯一名称，模型通过此名称调用。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 工具描述，供模型理解何时使用。
    /// </summary>
    string Description { get; }

    /// <summary>
    /// 参数 JSON Schema。
    /// </summary>
    JsonSchema Parameters { get; }

    /// <summary>
    /// 同轮并发资格。默认独占：语义未知的工具（含 MCP 适配）不参与并行。
    /// </summary>
    ToolConcurrency Concurrency => ToolConcurrency.Exclusive;

    /// <summary>
    /// 是否需要人工确认后才能执行（设备动作等有副作用的危险操作）。
    /// 编排器在执行前向宿主请求确认；宿主未提供确认回调时 fail-closed 拒绝执行。
    /// </summary>
    bool RequiresConfirmation => false;

    /// <summary>
    /// 执行工具调用，返回结果（含状态信息，回传给模型）。
    /// </summary>
    /// <param name="args">已解析的参数字典，可能为空。</param>
    /// <param name="ct">取消令牌。</param>
    Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default);
}