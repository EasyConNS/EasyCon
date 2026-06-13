using System.Text.Json;
using EasyCon.Core.LLM.Tools;
using EasyCon2.Avalonia.Core.Services;

namespace EasyCon2.Avalonia.Core.AiAgent.Tools;

/// <summary>
/// edit_script 工具：通过精确查找替换修改编辑区脚本内容。
/// 不会整块覆写，只改动指定的文本片段，适合精确修改。
/// </summary>
public class EditScriptTool : IAiTool
{
    private readonly IToolCallService _service;

    public EditScriptTool(IToolCallService service) => _service = service;

    public string Name => "edit_script";

    public string Description => "通过精确查找替换修改编辑区脚本。提供 old_string（要查找的文本）和 new_string（替换后的文本），实现精确修改而非整块覆写。count 可选，限制替换次数（默认全部替换）。";

    public JsonSchema Parameters => new()
    {
        Type = "object",
        Properties = new()
        {
            ["old_string"] = new JsonSchemaProperty
            {
                Type = "string",
                Description = "要查找的精确文本（必须完全匹配编辑区中的内容）。"
            },
            ["new_string"] = new JsonSchemaProperty
            {
                Type = "string",
                Description = "替换为的文本。为空字符串表示删除 old_string。"
            },
            ["count"] = new JsonSchemaProperty
            {
                Type = "integer",
                Description = "最大替换次数。省略或为 0 表示全部替换。"
            }
        },
        Required = ["old_string", "new_string"]
    };

    public Task<string> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        if (!args.TryGetValue("old_string", out var oldEl) || oldEl.ValueKind != JsonValueKind.String)
            return Task.FromResult("[错误] 缺少必填参数 old_string");

        if (!args.TryGetValue("new_string", out var newEl))
            return Task.FromResult("[错误] 缺少必填参数 new_string");

        var oldString = oldEl.GetString() ?? "";
        var newString = newEl.ValueKind == JsonValueKind.String ? (newEl.GetString() ?? "") : "";

        if (string.IsNullOrEmpty(oldString))
            return Task.FromResult("[错误] old_string 不能为空");

        var count = 0;
        if (args.TryGetValue("count", out var countEl) && countEl.ValueKind == JsonValueKind.Number && countEl.TryGetInt32(out var n))
            count = Math.Max(0, n);

        var replaced = _service.EditScriptContent(oldString, newString, count);

        if (replaced < 0)
            return Task.FromResult($"[未找到] 编辑区中未找到匹配的文本。请先用 read_script 确认内容。");

        var scope = count > 0 ? $"（限制替换 {count} 处）" : "（全部）";
        return Task.FromResult($"✅ 已替换 {replaced} 处{scope}：\n  \"{oldString}\" → \"{newString}\"");
    }
}
