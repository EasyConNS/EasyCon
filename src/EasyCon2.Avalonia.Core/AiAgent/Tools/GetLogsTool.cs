using System.Text.Json;
using EasyCon.Core.LLM.Tools;
using EasyCon2.Avalonia.Core.Services;

namespace EasyCon2.Avalonia.Core.AiAgent.Tools;

/// <summary>
/// get_logs 工具：返回最近的运行日志（按行）。
/// </summary>
public class GetLogsTool : IAiTool
{
    private readonly IToolCallService _service;

    public GetLogsTool(IToolCallService service) => _service = service;

    public string Name => "get_logs";

    public string Description => "获取最近的运行日志，用于排查编译错误或运行时问题。";

    public JsonSchema Parameters => new()
    {
        Type = "object",
        Properties = new()
        {
            ["lines"] = new JsonSchemaProperty
            {
                Type = "integer",
                Description = "返回的日志行数（默认 20，最大 200）。"
            }
        }
    };

    public Task<string> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        var lines = 20;
        if (args.TryGetValue("lines", out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n))
            lines = Math.Clamp(n, 1, 200);

        return Task.FromResult(_service.GetRecentLogs(lines));
    }
}
