using System.Text.Json;
using EasyCon.Core.LLM.Tools;
using EasyCon2.Avalonia.Core.Services;

namespace EasyCon2.Avalonia.Core.AiAgent.Tools;

/// <summary>
/// write_script 工具：将模型生成的脚本代码写入编辑区。
/// 支持覆盖全部内容或追加到末尾。
/// </summary>
public class WriteScriptTool : IAiTool
{
    private readonly IToolCallService _service;

    public WriteScriptTool(IToolCallService service) => _service = service;

    public string Name => "write_script";

    public string Description => "将脚本代码写入编辑区。mode 为 replace（默认）时替换全部内容，append 时追加到末尾。";

    public JsonSchema Parameters => new()
    {
        Type = "object",
        Properties = new()
        {
            ["content"] = new JsonSchemaProperty
            {
                Type = "string",
                Description = "要写入的脚本文本。"
            },
            ["mode"] = new JsonSchemaProperty
            {
                Type = "string",
                Description = "写入模式：replace（替换，默认）或 append（追加）。",
                Enum = ["replace", "append"]
            }
        },
        Required = ["content"]
    };

    public Task<string> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        if (!args.TryGetValue("content", out var contentEl) || contentEl.ValueKind != JsonValueKind.String)
            return Task.FromResult("[错误] 缺少必填参数 content");

        var content = contentEl.GetString() ?? "";
        var mode = args.TryGetValue("mode", out var modeEl) && modeEl.ValueKind == JsonValueKind.String
            ? modeEl.GetString()
            : "replace";
        var append = mode == "append";

        _service.WriteScriptContent(content, append);

        return Task.FromResult(append ? "✅ 已追加到编辑区末尾。" : "✅ 已替换编辑区内容。");
    }
}
