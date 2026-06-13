using System.Text.Json;
using EasyCon.Core.LLM.Tools;
using EasyCon2.Avalonia.Core.Services;

namespace EasyCon2.Avalonia.Core.AiAgent.Tools;

/// <summary>
/// format_script 工具：格式化当前编辑区脚本并写回编辑区。
/// 需脚本可编译通过，否则返回失败提示。
/// </summary>
public class FormatScriptTool : IAiTool
{
    private readonly IToolCallService _service;

    public FormatScriptTool(IToolCallService service) => _service = service;

    public string Name => "format_script";

    public string Description => "格式化当前编辑区的脚本并写回编辑区。要求脚本可编译通过，否则无法格式化。";

    public JsonSchema Parameters => new() { Type = "object" };

    public async Task<string> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        var formatted = await _service.FormatScriptAsync();
        return formatted.StartsWith("(")
            ? formatted
            : "✅ 格式化完成：\n" + formatted;
    }
}
