using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EasyCon.Core.LLM.Tools;
using EasyCon2.Avalonia.Core.Services;

namespace EasyCon2.Avalonia.Core.AiAgent.Tools;

/// <summary>
/// grep_script 工具：在编辑区脚本中按正则表达式搜索。
/// 返回匹配行号（含上下文行）和总出现次数。
/// </summary>
public class GrepScriptTool : IAiTool
{
    private readonly IToolCallService _service;

    public GrepScriptTool(IToolCallService service) => _service = service;

    public string Name => "grep_script";

    public string Description => "在编辑区脚本中按正则表达式搜索，返回匹配行号（含上下文行）和总出现次数。与 read_script 的区别是支持正则和精确行号定位。";

    public JsonSchema Parameters => new()
    {
        Type = "object",
        Properties = new()
        {
            ["pattern"] = new JsonSchemaProperty
            {
                Type = "string",
                Description = "正则表达式模式。例如 \"Key\\.A\" 匹配 Key.A，\"^loop\\b\" 匹配 loop 开头的行。"
            },
            ["context"] = new JsonSchemaProperty
            {
                Type = "integer",
                Description = "每条匹配上下文各取几行（默认 2）。"
            },
            ["ignore_case"] = new JsonSchemaProperty
            {
                Type = "boolean",
                Description = "是否忽略大小写（默认 false）。"
            },
            ["max_results"] = new JsonSchemaProperty
            {
                Type = "integer",
                Description = "最多返回多少条匹配（默认 50）。"
            }
        },
        Required = ["pattern"]
    };

    public Task<string> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        if (!args.TryGetValue("pattern", out var patEl) || patEl.ValueKind != JsonValueKind.String)
            return Task.FromResult("[错误] 缺少必填参数 pattern");

        var pattern = patEl.GetString() ?? "";
        var context = 2;
        var ignoreCase = false;
        var maxResults = 50;

        if (args.TryGetValue("context", out var ctxEl) && ctxEl.ValueKind == JsonValueKind.Number && ctxEl.TryGetInt32(out var ctx))
            context = Math.Clamp(ctx, 0, 10);

        if (args.TryGetValue("ignore_case", out var icEl) && icEl.ValueKind == JsonValueKind.True)
            ignoreCase = true;

        if (args.TryGetValue("max_results", out var mrEl) && mrEl.ValueKind == JsonValueKind.Number && mrEl.TryGetInt32(out var mr))
            maxResults = Math.Clamp(mr, 1, 200);

        var text = _service.GetScriptContent() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return Task.FromResult("(编辑区无脚本内容)");

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var totalLines = lines.Length;

        RegexOptions opts = RegexOptions.Compiled;
        if (ignoreCase) opts |= RegexOptions.IgnoreCase;

        Regex regex;
        try
        {
            regex = new Regex(pattern, opts);
        }
        catch (RegexParseException ex)
        {
            return Task.FromResult($"[错误] 正则表达式无效: {ex.Message}");
        }

        // 第一遍：收集匹配行号
        var matchLines = new List<int>(); // 0-based indices
        for (var i = 0; i < lines.Length; i++)
        {
            if (regex.IsMatch(lines[i]))
                matchLines.Add(i);
        }

        var totalCount = matchLines.Count;
        if (totalCount == 0)
            return Task.FromResult($"未找到匹配 \"{pattern}\"（共 {totalLines} 行）。");

        // 第二遍：合并上下文区间
        var displayCount = Math.Min(matchLines.Count, maxResults);
        var sb = new StringBuilder();
        sb.AppendLine($"共匹配 {totalCount} 行" + (displayCount < totalCount ? $"（显示前 {displayCount} 条）" : "") + "：");
        sb.AppendLine();

        var usedRanges = new List<(int start, int end)>();
        for (var idx = 0; idx < displayCount; idx++)
        {
            var lineIdx = matchLines[idx];
            var ctxStart = Math.Max(0, lineIdx - context);
            var ctxEnd = Math.Min(totalLines - 1, lineIdx + context);

            // 与上一个区间重叠则合并
            if (usedRanges.Count > 0)
            {
                var last = usedRanges[^1];
                if (ctxStart <= last.end + 1)
                {
                    usedRanges[^1] = (last.start, Math.Max(last.end, ctxEnd));
                    continue;
                }
            }
            usedRanges.Add((ctxStart, ctxEnd));
        }

        foreach (var (start, end) in usedRanges)
        {
            if (usedRanges.IndexOf((start, end)) > 0)
                sb.AppendLine("  ...");

            for (var i = start; i <= end; i++)
            {
                var marker = matchLines.Contains(i) ? ">" : " ";
                sb.Append($"  {marker} {i + 1,4} | {lines[i]}");
                if (i < end) sb.AppendLine();
            }
            sb.AppendLine();
        }

        if (displayCount < totalCount)
            sb.AppendLine($"  ...（还有 {totalCount - displayCount} 条匹配未显示）");

        return Task.FromResult(sb.ToString().TrimEnd());
    }
}
