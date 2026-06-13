using System.Text;
using System.Text.Json;
using EasyCon.Core.LLM.Tools;
using EasyCon2.Avalonia.Core.Services;

namespace EasyCon2.Avalonia.Core.AiAgent.Tools;

/// <summary>
/// read_script 工具：返回编辑区当前脚本文本。
/// 支持可选参数 start_line / end_line 按行号范围部分返回（1-based）。
/// </summary>
public class ReadScriptTool : IAiTool
{
    private readonly IToolCallService _service;

    public ReadScriptTool(IToolCallService service)
    {
        _service = service;
    }

    public string Name => "read_script";

    public string Description => "读取当前编辑区的脚本内容。可指定 start_line 和 end_line（1-based 行号）按行范围部分返回。";

    public JsonSchema Parameters => new()
    {
        Type = "object",
        Properties = new()
        {
            ["start_line"] = new JsonSchemaProperty
            {
                Type = "integer",
                Description = "起始行号（1-based，含）。省略则从头开始。"
            },
            ["end_line"] = new JsonSchemaProperty
            {
                Type = "integer",
                Description = "结束行号（1-based，含）。省略则到末尾。"
            }
        }
    };

    public Task<string> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        var text = _service.GetScriptContent() ?? string.Empty;
        var lines = text.Length == 0 ? [] : text.Replace("\r\n", "\n").Split('\n');

        var startLine = TryGetInt(args, "start_line", out var sl) ? sl : 1;
        var endLine = TryGetInt(args, "end_line", out var el) ? el : lines.Length;

        // 钳制范围到有效区间
        startLine = Math.Max(1, startLine);
        endLine = Math.Min(lines.Length, endLine);

        if (startLine > endLine || lines.Length == 0)
        {
            return Task.FromResult("(编辑区无脚本内容)");
        }

        // 带行号输出，便于模型定位
        var sb = new StringBuilder();
        for (var i = startLine; i <= endLine; i++)
        {
            sb.Append(i).Append(": ").Append(lines[i - 1]);
            if (i < endLine) sb.Append('\n');
        }

        var header = (startLine == 1 && endLine == lines.Length)
            ? $"共 {lines.Length} 行：\n"
            : $"第 {startLine}-{endLine} 行（共 {lines.Length} 行）：\n";

        return Task.FromResult(header + sb);
    }

    private static bool TryGetInt(Dictionary<string, JsonElement> args, string key, out int value)
    {
        value = 0;
        return args.TryGetValue(key, out var el)
            && el.ValueKind == JsonValueKind.Number
            && el.TryGetInt32(out value);
    }
}
