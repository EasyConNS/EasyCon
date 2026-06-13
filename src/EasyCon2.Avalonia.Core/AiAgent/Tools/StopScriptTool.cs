using System.Text.Json;
using EasyCon.Core.LLM.Tools;
using EasyCon2.Avalonia.Core.Services;

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

    public Task<string> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        if (!_service.IsScriptRunning)
            return Task.FromResult("当前没有运行中的脚本。");

        _service.StopScript();
        return Task.FromResult("脚本已停止。");
    }
}
