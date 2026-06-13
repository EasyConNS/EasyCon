using System.Text.Json;
using EasyCon.Core.LLM.Tools;

namespace EasyCon2.Avalonia.Core.AiAgent.Tools;

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
    /// 执行工具调用，返回结果文本（回传给模型）。
    /// </summary>
    /// <param name="args">已解析的参数字典，可能为空。</param>
    /// <param name="ct">取消令牌。</param>
    Task<string> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default);
}
