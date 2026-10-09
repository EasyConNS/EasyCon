using EasyCon.Core.LLM.Agent;
using EasyCon.Core.LLM.Agent.Tools;
using EasyCon.Core.LLM.Tools;
using EasyCon2.Avalonia.Services;
using System.Text.Json;

namespace EasyCon2.Avalonia.AiAgent.Tools;

/// <summary>
/// stop_script 工具：停止正在运行的脚本。
/// </summary>
public class StopScriptTool : IAiTool
{
    private readonly IScriptRunPort _run;

    public StopScriptTool(IScriptRunPort run) => _run = run;

    public string Name => "stop_script";

    public string Description => "停止正在运行的脚本。";

    public JsonSchema Parameters => new() { Type = "object" };

    public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        if (!_run.IsScriptRunning)
            return Task.FromResult(ToolResult.Ok("当前没有运行中的脚本。"));

        _run.StopScript();
        return Task.FromResult(ToolResult.Ok("脚本已停止。"));
    }
}