using EasyCon.Core.LLM.Tools;
using EasyCon2.Avalonia.Core.Services;
using System.Text.Json;

namespace EasyCon2.Avalonia.Core.AiAgent.Tools;

/// <summary>
/// stop_script 工具：停止正在运行的脚本。
/// </summary>
public class StopScriptTool : IAiTool
{
    private readonly IToolCallService _service;

    public StopScriptTool(IToolCallService service) => _service = service;

    public string Name => "stop_script";

    public string Description => "停止正在运行的脚本。";

    public JsonSchema Parameters => new() { Type = "object" };

    public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        if (!_service.IsScriptRunning)
            return Task.FromResult(ToolResult.Ok("当前没有运行中的脚本。"));

        _service.StopScript();
        return Task.FromResult(ToolResult.Ok("脚本已停止。"));
    }
}